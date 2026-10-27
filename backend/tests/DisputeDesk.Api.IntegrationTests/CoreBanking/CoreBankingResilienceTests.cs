using System.Diagnostics;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>The standard resilience handler on the core-banking client, exhausting to Unavailable (AC4).</summary>
public sealed class CoreBankingResilienceTests
{
    private const string CustomerA = "CUS-000001";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetTransactionsAsync_StubSlow_ReturnsUnavailableWithinFiveSeconds()
    {
        await using FakeCoreBankingServer slowStub = await FakeCoreBankingServer.SlowAsync();
        await using ServiceProvider services = CoreBankingServices.Build(slowStub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        var elapsed = Stopwatch.StartNew();
        Result<TransactionPage> result = await client.GetTransactionsAsync(CustomerA, cursor: null, limit: 20, CancellationToken);
        elapsed.Stop();

        Assert.Equal(CoreBankingErrors.Unavailable, result.Error);
        // The total timeout is 5 seconds; the margin covers scheduling on a busy machine.
        Assert.InRange(elapsed.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5.5));
    }

    [Fact]
    public async Task GetAccountsAsync_StubStallsTheBody_ReturnsUnavailableWithinFiveSeconds()
    {
        await using FakeCoreBankingServer stalledStub = await FakeCoreBankingServer.StalledBodyAsync("[]");
        await using ServiceProvider services = CoreBankingServices.Build(stalledStub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        var elapsed = Stopwatch.StartNew();
        Result<IReadOnlyList<Account>> result = await client.GetAccountsAsync(CustomerA, CancellationToken);
        elapsed.Stop();

        Assert.Equal(CoreBankingErrors.Unavailable, result.Error);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5.5), $"Took {elapsed.Elapsed}.");
    }

    [Fact]
    public async Task GetAccountsAsync_CallerCancels_ThrowsInsteadOfReturningUnavailable()
    {
        await using FakeCoreBankingServer slowStub = await FakeCoreBankingServer.SlowAsync();
        await using ServiceProvider services = CoreBankingServices.Build(slowStub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        caller.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAccountsAsync(CustomerA, caller.Token));
    }

    [Fact]
    public async Task MatchCustomerAsync_StubUnreachable_ReturnsUnavailableWithinFiveSeconds()
    {
        // Nothing listens on port 1, as when the stub is stopped.
        await using ServiceProvider services = CoreBankingServices.Build(new Uri("http://127.0.0.1:1"));
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        var elapsed = Stopwatch.StartNew();
        Result<CustomerMatch> result = await client.MatchCustomerAsync("9001015000085", "1000000011", CancellationToken);
        elapsed.Stop();

        Assert.Equal(CoreBankingErrors.Unavailable, result.Error);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5.5), $"Took {elapsed.Elapsed}.");
    }

    [Fact]
    public async Task GetAccountsAsync_StubFailing_RetriesTwiceThenReturnsUnavailable()
    {
        await using FakeCoreBankingServer failingStub = await FakeCoreBankingServer.FailingAsync();
        await using ServiceProvider services = CoreBankingServices.Build(failingStub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        Result<IReadOnlyList<Account>> result = await client.GetAccountsAsync(CustomerA, CancellationToken);

        Assert.Equal(CoreBankingErrors.Unavailable, result.Error);
        Assert.Equal(3, failingStub.RequestCount); // the first attempt and two retries
    }

    [Fact]
    public async Task GetAccountsAsync_StubAnswersMalformedJson_ReturnsUnavailable()
    {
        await using FakeCoreBankingServer malformedStub = await FakeCoreBankingServer.MalformedAsync();
        await using ServiceProvider services = CoreBankingServices.Build(malformedStub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        Result<IReadOnlyList<Account>> result = await client.GetAccountsAsync(CustomerA, CancellationToken);

        Assert.Equal(CoreBankingErrors.Unavailable, result.Error);
    }

    [Fact]
    public async Task GetAccountsAsync_AfterFiveFailures_CircuitOpensAndNoRequestIsSent()
    {
        await using FakeCoreBankingServer failingStub = await FakeCoreBankingServer.FailingAsync();
        await using ServiceProvider services = CoreBankingServices.Build(failingStub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        await client.GetAccountsAsync(CustomerA, CancellationToken); // 3 failed attempts
        await client.GetAccountsAsync(CustomerA, CancellationToken); // 2 more failures open the circuit; the third attempt is refused
        Result<IReadOnlyList<Account>> result = await client.GetAccountsAsync(CustomerA, CancellationToken);

        Assert.Equal(CoreBankingErrors.Unavailable, result.Error);
        Assert.Equal(5, failingStub.RequestCount);
    }

    [Fact]
    public async Task GetAccountsAsync_FailuresWithOneSuccessInTheWindow_CircuitStaysClosed()
    {
        // /accounts always fails with 500; anything else gets 404, which the circuit breaker counts as a success.
        await using FakeCoreBankingServer stub = await FakeCoreBankingServer.StartAsync(context =>
        {
            context.Response.StatusCode = context.Request.Path == "/accounts"
                ? StatusCodes.Status500InternalServerError
                : StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        await client.GetAccountsAsync(CustomerA, CancellationToken);                                  // 3 failures
        await client.GetTransactionAsync(CustomerA, "ACC-A-1", "TXN-A-0001", CancellationToken);    // 1 success
        await client.GetAccountsAsync(CustomerA, CancellationToken);                                  // 3 more failures
        int requestsBefore = stub.RequestCount;
        await client.GetAccountsAsync(CustomerA, CancellationToken);

        Assert.Equal(7, requestsBefore);
        Assert.Equal(requestsBefore + 3, stub.RequestCount); // the circuit is still closed, so every attempt reached the stub
    }

    [Fact]
    public void ResilienceOptions_CoreBankingClient_AreTunedForTheDemonstration()
    {
        using ServiceProvider services = CoreBankingServices.Build(new Uri("http://core-banking"));

        HttpStandardResilienceOptions options = services.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get($"{nameof(ICoreBankingClient)}-standard");

        Assert.Equal(TimeSpan.FromSeconds(2), options.AttemptTimeout.Timeout);
        Assert.Equal(2, options.Retry.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(200), options.Retry.Delay);
        Assert.True(options.Retry.UseJitter);
        Assert.Equal(TimeSpan.FromSeconds(5), options.TotalRequestTimeout.Timeout);
        Assert.Equal(1.0, options.CircuitBreaker.FailureRatio);
        Assert.Equal(5, options.CircuitBreaker.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(30), options.CircuitBreaker.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(15), options.CircuitBreaker.BreakDuration);
    }
}
