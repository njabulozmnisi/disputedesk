using System.Diagnostics;
using System.Net;
using System.Text.Json;
using DisputeDesk.Api.Errors;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests;

public class ProblemDetailsTests(PostgresFixture postgres)
{
    private const string ProblemJson = "application/problem+json";
    private const string SecretDetail = ThrowingHealthCheckService.SecretDetail;

    [Fact]
    public async Task UnknownRoute_Get_Returns404ProblemDetailsWithTraceId()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/api/v1/no-such-route", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = await ReadJsonAsync(response);
        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task UnknownRoute_Get_TraceIdMatchesServerSpan()
    {
        var telemetry = new TelemetryCapture();
        await using var factory = new ApiFactory(postgres.ConnectionString, configureServices: telemetry.AddTo);
        using HttpClient client = factory.CreateClient();
        string path = $"/api/v1/trace-probe-{Guid.NewGuid():N}";

        HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument problem = await ReadJsonAsync(response);
        // The request's own span, found by its unique path: also the proof that ASP.NET Core requests are traced.
        Activity span = await telemetry.WaitForSpanAsync(activity => Equals(activity.GetTagItem("url.path"), path));
        Assert.Equal(ActivityKind.Server, span.Kind);
        Assert.Equal("Microsoft.AspNetCore", span.Source.Name);
        Assert.Equal("GET", span.GetTagItem("http.request.method"));
        Assert.Equal(404, span.GetTagItem("http.response.status_code"));
        Assert.Equal(span.TraceId.ToHexString(), problem.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task TryHandleAsync_UnhandledException_TraceIdMatchesServerSpan()
    {
        var telemetry = new TelemetryCapture();
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                telemetry.AddTo(services);
                ThrowingHealthCheckService.Register(services);
            });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using JsonDocument problem = await ReadJsonAsync(response);
        string? traceId = problem.RootElement.GetProperty("traceId").GetString();
        // Other tests' server spans can reach this exporter, so the match is on this response's trace ID.
        Activity span = await telemetry.WaitForSpanAsync(activity => activity.Kind == ActivityKind.Server && activity.TraceId.ToHexString() == traceId);
        Assert.Equal("/health/live", span.GetTagItem("url.path"));
        Assert.Equal(500, span.GetTagItem("http.response.status_code"));
    }

    [Fact]
    public async Task TryHandleAsync_UnhandledException_ReturnsClean500()
    {
        // Development is the worst case: it is where the developer exception page would leak details.
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            environment: "Development",
            configureServices: ThrowingHealthCheckService.Register);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(SecretDetail, body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ThrowingHealthCheckService), body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);

        using var problem = JsonDocument.Parse(body);
        JsonElement root = problem.RootElement;
        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", root.GetProperty("title").GetString());
        Assert.False(root.TryGetProperty("detail", out _));
        Assert.False(root.TryGetProperty("exception", out _));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }

    public static TheoryData<Error, int> ErrorsAndStatuses => new()
    {
        // AD-3: the status each kind maps to, one row per kind, codes from the slug catalogue.
        { Error.Validation("validation", "The note is longer than 500 characters.", "Note"), StatusCodes.Status400BadRequest },
        { Error.Forbidden("forbidden", "Not allowed."), StatusCodes.Status403Forbidden },
        { Error.NotFound("not-found", "Dispute not found."), StatusCodes.Status404NotFound },
        { Error.ActionNotAllowed("action-not-allowed", "A resolved dispute cannot be withdrawn.", "customerStatus", SampleCustomerStatus.Resolved), StatusCodes.Status409Conflict },
        { Error.Conflict("already-disputed", "This transaction already has an open dispute."), StatusCodes.Status409Conflict },
        { Error.BusinessRule("not-disputable", "The transaction is older than 90 days."), StatusCodes.Status422UnprocessableEntity },
        { Error.Unavailable("core-banking-unavailable", "Core banking is unavailable."), StatusCodes.Status503ServiceUnavailable },
    };

    [Theory]
    [MemberData(nameof(ErrorsAndStatuses))]
    public async Task ToProblem_EachErrorKind_WritesProblemDetailsWithItsStatus(Error error, int expectedStatus)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Response.Body = new MemoryStream();

        await error.ToProblem().ExecuteAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.StartsWith(ProblemJson, context.Response.ContentType, StringComparison.Ordinal);
        context.Response.Body.Position = 0;
        using JsonDocument problem = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        JsonElement root = problem.RootElement;
        Assert.Equal(expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(error.Message, root.GetProperty("detail").GetString());
        Assert.Equal($"tag:disputedesk,2026:problem:{error.Code}", root.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task ToProblem_InsideActivity_CarriesCurrentTraceId()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Response.Body = new MemoryStream();
        using Activity activity = new Activity("expected-failure").Start();

        await Error.NotFound("not-found", "Dispute not found.").ToProblem().ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using JsonDocument problem = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(activity.TraceId.ToHexString(), problem.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public void ErrorsAndStatuses_AllKinds_HaveOneRowEach()
    {
        IEnumerable<ErrorKind> proved = ErrorsAndStatuses.Select(row => row.Data.Item1.Kind);

        Assert.Equal(Enum.GetValues<ErrorKind>().Order(), proved.Order());
    }

    [Theory]
    [MemberData(nameof(ErrorsAndStatuses))]
    public void StatusCodeFor_EachErrorKind_ReturnsItsAd3Status(Error error, int expectedStatus)
    {
        Assert.Equal(expectedStatus, ErrorResults.StatusCodeFor(error.Kind));
    }

    [Fact]
    public async Task ToProblem_Validation_Writes400WithCamelCaseFieldErrors()
    {
        var error = Error.Validation("validation", "The note is longer than 1000 characters.", "Note");

        using JsonDocument problem = await ExecuteAsync(error.ToProblem());

        JsonElement root = problem.RootElement;
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("tag:disputedesk,2026:problem:validation", root.GetProperty("type").GetString());
        Assert.Equal("The note is longer than 1000 characters.", root.GetProperty("errors").GetProperty("note")[0].GetString());
        Assert.False(root.GetProperty("errors").TryGetProperty("Note", out _));
    }

    [Fact]
    public async Task ToProblem_ActionNotAllowed_Writes409WithTheCurrentStatusAsACamelCaseValue()
    {
        var error = Error.ActionNotAllowed("action-not-allowed", "This dispute can no longer be withdrawn.", "customerStatus", SampleCustomerStatus.UnderReviewPrioritised);

        using JsonDocument problem = await ExecuteAsync(error.ToProblem());

        JsonElement root = problem.RootElement;
        Assert.Equal(409, root.GetProperty("status").GetInt32());
        Assert.Equal("tag:disputedesk,2026:problem:action-not-allowed", root.GetProperty("type").GetString());
        Assert.Equal("underReviewPrioritised", root.GetProperty("customerStatus").GetString());
    }

    [Fact]
    public async Task Login_ReturnUrlTooLong_Returns400FromBuiltInValidationWithoutEchoingIt()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient client = factory.CreateClient();
        string marker = "echo-marker-" + new string('x', 2048);

        HttpResponseMessage response = await client.GetAsync(new Uri($"/api/v1/bff/login?returnUrl=/{marker}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("echo-marker", body, StringComparison.Ordinal);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("tag:disputedesk,2026:problem:validation", problem.RootElement.GetProperty("type").GetString());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("returnUrl", out _));
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("/api/v1/no-such-route", 404, "not-found")]
    [InlineData("/api/v1/bff/antiforgery", 401, "unauthorized")]
    [InlineData(ForbidProbe.Path, 403, "forbidden")]
    public async Task FrameworkResponse_EachStatus_CarriesItsSlug(string path, int expectedStatus, string expectedSlug)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, configureServices: ForbidProbe.Register);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = await ReadJsonAsync(response);
        Assert.Equal($"tag:disputedesk,2026:problem:{expectedSlug}", problem.RootElement.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task TryHandleAsync_UnhandledException_CarriesTheInternalSlug()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, configureServices: ThrowingHealthCheckService.Register);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        using JsonDocument problem = await ReadJsonAsync(response);
        Assert.Equal("tag:disputedesk,2026:problem:internal", problem.RootElement.GetProperty("type").GetString());
    }

    private enum SampleCustomerStatus
    {
        UnderReviewPrioritised,
        Resolved,
    }

    private async Task<JsonDocument> ExecuteAsync(ProblemHttpResult result)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
}
