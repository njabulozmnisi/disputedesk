using DisputeDesk.Application.Abstractions;
using DisputeDesk.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>
/// 7.13 AC3 (ASVS 5.0.0 V15.3.2): neither banking client follows a redirect, so the customer ID header, the account
/// reference and the request body never reach the origin a redirect names. A redirect is a non-success answer like any
/// other: core banking unavailable, and the SIM-swap check fails closed.
/// </summary>
public sealed class RedirectTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(302)]
    [InlineData(307)]
    public async Task GetTransactionAsync_StubRedirectsToAnotherOrigin_ReturnsUnavailableAndSendsNothingThere(int status)
    {
        await using FakeCoreBankingServer elsewhere = await FakeCoreBankingServer.StartAsync(_ => Task.CompletedTask);
        await using FakeCoreBankingServer redirecting = await FakeCoreBankingServer.RedirectingAsync(status, elsewhere.BaseUrl);
        await using ServiceProvider services = CoreBankingServices.Build(redirecting.BaseUrl);

        Result<Transaction> result = await services.GetRequiredService<ICoreBankingClient>()
            .GetTransactionAsync("CUS-000001", "ACC-A-1", "TXN-A-0029", CancellationToken);

        Assert.Equal(CoreBankingErrors.Unavailable, result.Error);
        Assert.Equal(1, redirecting.RequestCount);
        Assert.Equal(0, elsewhere.RequestCount);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(307)]
    public async Task MatchCustomerAsync_StubRedirectsToAnotherOrigin_ReturnsUnavailableAndSendsNothingThere(int status)
    {
        // A 307 keeps the method and the body, so following it would send the ID number on.
        await using FakeCoreBankingServer elsewhere = await FakeCoreBankingServer.StartAsync(_ => Task.CompletedTask);
        await using FakeCoreBankingServer redirecting = await FakeCoreBankingServer.RedirectingAsync(status, elsewhere.BaseUrl);
        await using ServiceProvider services = CoreBankingServices.Build(redirecting.BaseUrl);

        Result<CustomerMatch> result = await services.GetRequiredService<ICoreBankingClient>()
            .MatchCustomerAsync("0000000000000", "0000000000", CancellationToken);

        Assert.Equal(CoreBankingErrors.Unavailable, result.Error);
        Assert.Equal(1, redirecting.RequestCount);
        Assert.Equal(0, elsewhere.RequestCount);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(307)]
    public async Task PassesAsync_StubRedirectsToAnotherOrigin_ReportsFailedAndSendsNothingThere(int status)
    {
        await using FakeCoreBankingServer elsewhere = await FakeCoreBankingServer.StartAsync(_ => Task.CompletedTask);
        await using FakeCoreBankingServer redirecting = await FakeCoreBankingServer.RedirectingAsync(status, elsewhere.BaseUrl);
        await using ServiceProvider services = CoreBankingServices.Build(redirecting.BaseUrl);

        bool passes = await services.GetRequiredService<ISimSwapCheck>().PassesAsync("+27820000004", CancellationToken);

        Assert.False(passes);
        Assert.Equal(1, redirecting.RequestCount);
        Assert.Equal(0, elsewhere.RequestCount);
    }
}
