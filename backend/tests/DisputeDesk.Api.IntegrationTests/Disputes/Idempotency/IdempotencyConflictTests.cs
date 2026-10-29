using System.Net;
using DisputeDesk.Api.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Disputes.RaiseDispute;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes.Idempotency;

/// <summary>
/// AC3: a different request under a used key gets 422 <c>idempotency-mismatch</c>; the same request while the first
/// holds the key gets 409 <c>idempotency-in-progress</c>; an in-progress key older than 60 seconds with the same hash
/// is taken over under a new lease; the raise times out after 30 seconds with 504 and releases its key.
/// </summary>
public class IdempotencyConflictTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    // The handler's read of an earlier dispute: a raise holds its key while it waits here.
    private const string HandlerRead = "FROM \"Disputes\"";

    [Theory]
    [InlineData("""{"accountRef":"ACC-A-1","transactionId":"TXN-A-0028","reason":"unrecognised"}""")]                         // another transaction
    [InlineData("""{"accountRef":"ACC-A-1","transactionId":"TXN-A-0029","reason":"duplicateCharge"}""")]                      // another reason
    [InlineData("""{"accountRef":"ACC-A-1","transactionId":"TXN-A-0029","reason":"unrecognised","note":"x"}""")]              // a note added
    public async Task Raise_DifferentRequestUnderACompletedKey_Returns422IdempotencyMismatchAndWritesNothingMore(string different)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string key = DisputesApi.NewKey();
        Assert.Equal(HttpStatusCode.Created, (await browser.RaiseWithKeyAsync(DisputesApi.Body(), key)).StatusCode);

        HttpResponseMessage response = await browser.RaiseWithKeyAsync(different, key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ProblemType.For(DisputeProblems.IdempotencyMismatch), await response.ProblemTypeAsync());
        Assert.Equal(RowCounts.One, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Raise_SameAmountWrittenDifferently_IsADifferentRequest()
    {
        // Amounts are compared as sent: 80.0 and 80.00 are the same money but not the same request.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string key = DisputesApi.NewKey();
        await browser.RaiseWithKeyAsync(DisputesApi.Body(reason: "wrongAmount", expectedAmount: "80.00"), key);

        HttpResponseMessage response = await browser.RaiseWithKeyAsync(DisputesApi.Body(reason: "wrongAmount", expectedAmount: "80.0"), key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Raise_SameRequestWhileTheFirstHoldsTheKey_Returns409InProgressThenTheFirstCompletes()
    {
        var hold = new HoldInterceptor(HandlerRead);
        await using ApiFactory factory = await StartAsync(hold);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string token = await browser.AntiforgeryTokenAsync();
        string key = DisputesApi.NewKey();
        Task<HttpResponseMessage> first = browser.RaiseAsync(DisputesApi.Body(), token, key);
        await hold.Reached;

        HttpResponseMessage second = await browser.RaiseAsync(DisputesApi.Body(), token, key);
        hold.Release();

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ProblemType.For(DisputeProblems.IdempotencyInProgress), await second.ProblemTypeAsync());
        Assert.Equal(HttpStatusCode.Created, (await first).StatusCode);
        Assert.Equal(RowCounts.One, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Raise_DifferentRequestWhileTheFirstHoldsTheKey_Returns422Mismatch()
    {
        var hold = new HoldInterceptor(HandlerRead);
        await using ApiFactory factory = await StartAsync(hold);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string token = await browser.AntiforgeryTokenAsync();
        string key = DisputesApi.NewKey();
        Task<HttpResponseMessage> first = browser.RaiseAsync(DisputesApi.Body(), token, key);
        await hold.Reached;

        HttpResponseMessage second = await browser.RaiseAsync(DisputesApi.Body(reason: "duplicateCharge"), token, key);
        hold.Release();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await first).StatusCode);
    }

    [Theory]
    [InlineData(0)]   // exactly 60 seconds old: not older than 60 seconds
    [InlineData(-1)]  // a second younger
    public async Task Raise_SameRequestUnderAnInProgressKeyAtMost60SecondsOld_Returns409InProgress(int secondsPast60)
    {
        var hold = new HoldInterceptor(HandlerRead);
        await using ApiFactory factory = await StartAsync(hold);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string token = await browser.AntiforgeryTokenAsync();
        string key = DisputesApi.NewKey();
        Task<HttpResponseMessage> first = browser.RaiseAsync(DisputesApi.Body(), token, key);
        await hold.Reached;
        await factory.SetLeaseTakenAtAsync(stub.Now - IdempotencyKey.TakeOverAfter - TimeSpan.FromSeconds(secondsPast60));

        HttpResponseMessage second = await browser.RaiseAsync(DisputesApi.Body(), token, key);
        hold.Release();

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await first).StatusCode);
    }

    [Fact]
    public async Task Raise_SameRequestUnderAnInProgressKeyOlderThan60Seconds_TakesItOverAndTheOldLeaseCannotOverwriteIt()
    {
        var hold = new HoldInterceptor(HandlerRead);
        await using ApiFactory factory = await StartAsync(hold);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string token = await browser.AntiforgeryTokenAsync();
        string key = DisputesApi.NewKey();
        Task<HttpResponseMessage> stale = browser.RaiseAsync(DisputesApi.Body(), token, key);
        await hold.Reached;
        Guid staleLease = Assert.Single(await factory.KeyRowsAsync()).LeaseId;
        // One microsecond (Postgres's precision) past the 60 seconds.
        await factory.SetLeaseTakenAtAsync(stub.Now - IdempotencyKey.TakeOverAfter - TimeSpan.FromTicks(10));

        HttpResponseMessage takeOver = await browser.RaiseAsync(DisputesApi.Body(), token, key);
        hold.Release();
        HttpResponseMessage staleResponse = await stale;

        Assert.Equal(HttpStatusCode.Created, takeOver.StatusCode);
        Assert.NotEqual(HttpStatusCode.Created, staleResponse.StatusCode);
        IdempotencyKey row = Assert.Single(await factory.KeyRowsAsync());
        Assert.NotEqual(staleLease, row.LeaseId);
        Assert.Equal(201, row.ResponseStatus);
        Assert.Equal(RowCounts.One, await factory.CountRowsAsync());
        await IdempotencyRows.AssertSameResponseAsync(takeOver, await browser.RaiseAsync(DisputesApi.Body(), token, key));
    }

    [Fact]
    public async Task Raise_DifferentRequestUnderAnInProgressKeyOlderThan60Seconds_IsNotTakenOver()
    {
        var hold = new HoldInterceptor(HandlerRead);
        await using ApiFactory factory = await StartAsync(hold);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string token = await browser.AntiforgeryTokenAsync();
        string key = DisputesApi.NewKey();
        Task<HttpResponseMessage> first = browser.RaiseAsync(DisputesApi.Body(), token, key);
        await hold.Reached;
        await factory.SetLeaseTakenAtAsync(stub.Now - TimeSpan.FromMinutes(5));

        HttpResponseMessage different = await browser.RaiseAsync(DisputesApi.Body(reason: "duplicateCharge"), token, key);
        hold.Release();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, different.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await first).StatusCode);
    }

    [Fact]
    public async Task RaiseEndpoint_RequestTimeout_Is30Seconds()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        RouteEndpoint raise = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/api/v1/disputes" && endpoint.DisplayName?.StartsWith("HTTP: POST", StringComparison.Ordinal) == true);
        RequestTimeoutOptions options = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestTimeoutOptions>>().Value;

        Assert.Equal(DisputeEndpoints.RaiseTimeoutPolicy, raise.Metadata.GetRequiredMetadata<RequestTimeoutAttribute>().PolicyName);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Policies[DisputeEndpoints.RaiseTimeoutPolicy].Timeout);
    }

    [Fact]
    public async Task Raise_TimesOut_Returns504AndReleasesTheKeyWithNothingWritten()
    {
        // The handler passes the request's token on, so the held read is cancelled when the timeout fires. The
        // framework's timer cannot be faked, so the policy is shortened to one second; this is the suite's one real wait.
        var hold = new HoldInterceptor(HandlerRead);
        await using ApiFactory factory = await StartAsync(hold, services => services.Configure<RequestTimeoutOptions>(
            options => options.AddPolicy(DisputeEndpoints.RaiseTimeoutPolicy, TimeSpan.FromSeconds(1))));
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string key = DisputesApi.NewKey();

        HttpResponseMessage response = await browser.RaiseWithKeyAsync(DisputesApi.Body(), key);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Empty(await factory.KeyRowsAsync());
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
    }

    private Task<ApiFactory> StartAsync(HoldInterceptor hold, Action<IServiceCollection>? configure = null) =>
        TransactionsApi.StartAsync(postgres, stub, services =>
        {
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(hold));
            configure?.Invoke(services);
        });
}
