using System.Diagnostics;
using System.Net;
using System.Text.Json;
using DisputeDesk.Api.Errors;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests;

public class ProblemDetailsTests(PostgresFixture postgres)
{
    private const string ProblemJson = "application/problem+json";
    private const string SecretDetail = ThrowingHealthCheckService.SecretDetail;

    [Fact]
    public async Task Unknown_route_returns_a_404_ProblemDetails_with_a_traceId()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/no-such-route", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var problem = await ReadJsonAsync(response);
        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task The_404_traceId_is_the_trace_ID_of_the_request_server_span()
    {
        var telemetry = new TelemetryCapture();
        await using var factory = new ApiFactory(postgres.ConnectionString, configureServices: telemetry.AddTo);
        using var client = factory.CreateClient();
        var path = $"/api/v1/trace-probe-{Guid.NewGuid():N}";

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var problem = await ReadJsonAsync(response);
        // The request's own span, found by its unique path: also the proof that ASP.NET Core requests are traced.
        var span = await telemetry.WaitForSpanAsync(activity => Equals(activity.GetTagItem("url.path"), path));
        Assert.Equal(ActivityKind.Server, span.Kind);
        Assert.Equal("Microsoft.AspNetCore", span.Source.Name);
        Assert.Equal("GET", span.GetTagItem("http.request.method"));
        Assert.Equal(404, span.GetTagItem("http.response.status_code"));
        Assert.Equal(span.TraceId.ToHexString(), problem.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task The_500_traceId_is_the_trace_ID_of_the_request_server_span()
    {
        var telemetry = new TelemetryCapture();
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                telemetry.AddTo(services);
                ThrowingHealthCheckService.Register(services);
            });
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var problem = await ReadJsonAsync(response);
        var traceId = problem.RootElement.GetProperty("traceId").GetString();
        // Other tests' server spans can reach this exporter, so the match is on this response's trace ID.
        var span = await telemetry.WaitForSpanAsync(activity => activity.Kind == ActivityKind.Server && activity.TraceId.ToHexString() == traceId);
        Assert.Equal("/health/live", span.GetTagItem("url.path"));
        Assert.Equal(500, span.GetTagItem("http.response.status_code"));
    }

    [Fact]
    public async Task Unhandled_exception_returns_a_clean_500_without_message_or_stack_trace()
    {
        // Development is the worst case: it is where the developer exception page would leak details.
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            environment: "Development",
            configureServices: ThrowingHealthCheckService.Register);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(SecretDetail, body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ThrowingHealthCheckService), body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);

        using var problem = JsonDocument.Parse(body);
        var root = problem.RootElement;
        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", root.GetProperty("title").GetString());
        Assert.False(root.TryGetProperty("detail", out _));
        Assert.False(root.TryGetProperty("exception", out _));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }

    public static TheoryData<Error, int> ErrorsAndStatuses => new()
    {
        { Error.NotFound("dispute.not_found", "Dispute not found."), StatusCodes.Status404NotFound },
        { Error.InvalidTransition("dispute.invalid_transition", "A resolved dispute cannot be withdrawn."), StatusCodes.Status409Conflict },
        { Error.Validation("dispute.reason_required", "A reason is required."), StatusCodes.Status422UnprocessableEntity },
        { Error.BusinessRule("dispute.window_closed", "The dispute window has closed."), StatusCodes.Status422UnprocessableEntity },
    };

    [Theory]
    [MemberData(nameof(ErrorsAndStatuses))]
    public async Task Each_error_kind_maps_to_its_status_as_ProblemDetails(Error error, int expectedStatus)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Response.Body = new MemoryStream();

        await error.ToProblem().ExecuteAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.StartsWith(ProblemJson, context.Response.ContentType, StringComparison.Ordinal);
        context.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        var root = problem.RootElement;
        Assert.Equal(expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(error.Message, root.GetProperty("detail").GetString());
        Assert.Equal(error.Code, root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task An_expected_failure_carries_the_current_trace_ID()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Response.Body = new MemoryStream();
        using var activity = new Activity("expected-failure").Start();

        await Error.NotFound("dispute.not_found", "Dispute not found.").ToProblem().ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(activity.TraceId.ToHexString(), problem.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public void Every_error_kind_has_a_status()
    {
        Assert.All(Enum.GetValues<ErrorKind>(), kind => Assert.InRange(ErrorResults.StatusCodeFor(kind), 400, 499));
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
}
