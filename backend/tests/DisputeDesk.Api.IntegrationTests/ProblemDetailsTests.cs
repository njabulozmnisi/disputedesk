using System.Diagnostics;
using System.Net;
using System.Text.Json;
using DisputeDesk.Api.Errors;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests;

public class ProblemDetailsTests(PostgresFixture postgres)
{
    private const string ProblemJson = "application/problem+json";
    private const string SecretDetail = ThrowingHealthCheckService.SecretDetail;

    // Every test that starts the Api runs in both, so no Development-only behaviour hides a failure (2.8 AC10).
    private static readonly string[] Environments = ["Development", "Production"];

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task UnknownRoute_Get_Returns404ProblemDetailsWithTraceId(string environment)
    {
        // Signed in: an anonymous request that matches no endpoint gets the fallback policy's 401 instead (2.8 AC9).
        await using var factory = new ApiFactory(postgres.ConnectionString, environment);
        using HttpClient client = await SignedInBrowserAsync(factory);

        HttpResponseMessage response = await client.GetAsync(new Uri("/api/v1/no-such-route", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = await ReadJsonAsync(response);
        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task UnknownRoute_GetAnonymous_Returns401ProblemDetailsWithTraceId(string environment)
    {
        // The fallback policy applies to a request with no endpoint, so route existence stays hidden (2.8 AC9).
        await using var factory = new ApiFactory(postgres.ConnectionString, environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/api/v1/no-such-route", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = await ReadJsonAsync(response);
        Assert.Equal(401, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(ProblemType.For(CommonProblems.Unauthorized), problem.RootElement.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task UnknownRoute_Get_TraceIdMatchesServerSpan(string environment)
    {
        var telemetry = new TelemetryCapture();
        await using var factory = new ApiFactory(postgres.ConnectionString, environment, configureServices: telemetry.AddTo);
        using HttpClient client = await SignedInBrowserAsync(factory);
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

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task UnknownRoute_GetAnonymous_TraceIdMatchesServerSpan(string environment)
    {
        var telemetry = new TelemetryCapture();
        await using var factory = new ApiFactory(postgres.ConnectionString, environment, configureServices: telemetry.AddTo);
        using HttpClient client = factory.CreateClient();
        string path = $"/api/v1/trace-probe-{Guid.NewGuid():N}";

        HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using JsonDocument problem = await ReadJsonAsync(response);
        Activity span = await telemetry.WaitForSpanAsync(activity => Equals(activity.GetTagItem("url.path"), path));
        Assert.Equal(ActivityKind.Server, span.Kind);
        Assert.Equal(401, span.GetTagItem("http.response.status_code"));
        Assert.Equal(span.TraceId.ToHexString(), problem.RootElement.GetProperty("traceId").GetString());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task TryHandleAsync_UnhandledException_TraceIdMatchesServerSpan(string environment)
    {
        var telemetry = new TelemetryCapture();
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            environment,
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

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task TryHandleAsync_UnhandledException_ReturnsClean500(string environment)
    {
        // Development is the worst case: it is where the developer exception page would leak details.
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            environment,
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
        { Error.RateLimited("rate-limited", "Too many linking starts are waiting."), StatusCodes.Status429TooManyRequests },
    };

    public static TheoryData<Error, int, string> ErrorsAndStatusesInEachEnvironment()
    {
        var data = new TheoryData<Error, int, string>();
        foreach (string environment in Environments)
        {
            foreach (TheoryDataRow<Error, int> row in ErrorsAndStatuses)
            {
                data.Add(row.Data.Item1, row.Data.Item2, environment);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ErrorsAndStatusesInEachEnvironment))]
    public async Task ToProblem_EachErrorKind_WritesProblemDetailsWithItsStatus(Error error, int expectedStatus, string environment)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment);
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
        Assert.Equal($"https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/{error.Code}.md", root.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task ToProblem_InsideActivity_CarriesCurrentTraceId(string environment)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment);
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

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task ToProblem_Validation_Writes400WithCamelCaseFieldErrors(string environment)
    {
        var error = Error.Validation("validation", "The note is longer than 1000 characters.", "Note");

        using JsonDocument problem = await ExecuteAsync(error.ToProblem(), environment);

        JsonElement root = problem.RootElement;
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/validation.md", root.GetProperty("type").GetString());
        Assert.Equal("The note is longer than 1000 characters.", root.GetProperty("errors").GetProperty("note")[0].GetString());
        Assert.False(root.GetProperty("errors").TryGetProperty("Note", out _));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task ToProblem_ActionNotAllowed_Writes409WithTheCurrentStatusAsACamelCaseValue(string environment)
    {
        var error = Error.ActionNotAllowed("action-not-allowed", "This dispute can no longer be withdrawn.", "customerStatus", SampleCustomerStatus.UnderReviewPrioritised);

        using JsonDocument problem = await ExecuteAsync(error.ToProblem(), environment);

        JsonElement root = problem.RootElement;
        Assert.Equal(409, root.GetProperty("status").GetInt32());
        Assert.Equal("https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/action-not-allowed.md", root.GetProperty("type").GetString());
        Assert.Equal("underReviewPrioritised", root.GetProperty("customerStatus").GetString());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Login_ReturnUrlTooLong_Returns400FromBuiltInValidationWithoutEchoingIt(string environment)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment);
        using HttpClient client = factory.CreateClient();
        string marker = "echo-marker-" + new string('x', 2048);

        HttpResponseMessage response = await client.GetAsync(new Uri($"/api/v1/bff/login?returnUrl=/{marker}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("echo-marker", body, StringComparison.Ordinal);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/validation.md", problem.RootElement.GetProperty("type").GetString());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("returnUrl", out _));
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    public static TheoryData<string, bool, int, string, string> FrameworkResponses()
    {
        // The probes sit after authorization on paths with no endpoint, so only a signed-in caller reaches them (2.8 AC9).
        var data = new TheoryData<string, bool, int, string, string>();
        foreach (string environment in Environments)
        {
            data.Add("/api/v1/no-such-route", true, 404, "not-found", environment);
            data.Add("/api/v1/no-such-route", false, 401, "unauthorized", environment);
            data.Add("/api/v1/bff/antiforgery", false, 401, "unauthorized", environment);
            data.Add(ForbidProbe.Path, true, 403, "forbidden", environment);
            data.Add(ForbidProbe.Path, false, 401, "unauthorized", environment);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FrameworkResponses))]
    public async Task FrameworkResponse_EachStatus_CarriesItsSlug(string path, bool signedIn, int expectedStatus, string expectedSlug, string environment)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment, configureServices: ForbidProbe.Register);
        using HttpClient client = signedIn ? await SignedInBrowserAsync(factory) : factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = await ReadJsonAsync(response);
        Assert.Equal($"https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/{expectedSlug}.md", problem.RootElement.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task TryHandleAsync_UnhandledException_CarriesTheInternalSlug(string environment)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment, configureServices: ThrowingHealthCheckService.Register);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        using JsonDocument problem = await ReadJsonAsync(response);
        Assert.Equal("https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/internal.md", problem.RootElement.GetProperty("type").GetString());
    }

    private enum SampleCustomerStatus
    {
        UnderReviewPrioritised,
        Resolved,
    }

    private async Task<JsonDocument> ExecuteAsync(ProblemHttpResult result, string environment)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task<HttpClient> SignedInBrowserAsync(ApiFactory factory)
    {
        HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, $"problem-{Guid.NewGuid():N}");
        return browser;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
}
