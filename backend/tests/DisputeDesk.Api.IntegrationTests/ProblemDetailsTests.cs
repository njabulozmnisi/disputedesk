using System.Net;
using System.Text.Json;
using DisputeDesk.Api.Errors;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DisputeDesk.Api.IntegrationTests;

public class ProblemDetailsTests(PostgresFixture postgres)
{
    private const string ProblemJson = "application/problem+json";
    private const string SecretDetail = "secret-detail-that-must-not-leak";

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
    public async Task Unhandled_exception_returns_a_clean_500_without_message_or_stack_trace()
    {
        // Development is the worst case: it is where the developer exception page would leak details.
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            environment: "Development",
            configureServices: services => services.AddSingleton<HealthCheckService, ThrowingHealthCheckService>());
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
    public void Every_error_kind_has_a_status()
    {
        Assert.All(Enum.GetValues<ErrorKind>(), kind => Assert.InRange(ErrorResults.StatusCodeFor(kind), 400, 499));
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);

    private sealed class ThrowingHealthCheckService : HealthCheckService
    {
        public override Task<HealthReport> CheckHealthAsync(
            Func<HealthCheckRegistration, bool>? predicate,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(SecretDetail);
    }
}
