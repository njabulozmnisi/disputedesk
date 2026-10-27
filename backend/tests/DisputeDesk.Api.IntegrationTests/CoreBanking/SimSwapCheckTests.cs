using System.Diagnostics;
using DisputeDesk.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>The SIM-swap check fails closed (AC5).</summary>
public sealed class SimSwapCheckTests(CoreBankingStubFactory stub) : IClassFixture<CoreBankingStubFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("+27820000003", false)] // customer C: a recent SIM swap in the fixtures
    [InlineData("+27820000004", true)]  // customer D: no recent swap
    public async Task PassesAsync_CellphoneOnRecord_FollowsTheFixtures(string cellphone, bool expected)
    {
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);

        bool passes = await services.GetRequiredService<ISimSwapCheck>().PassesAsync(cellphone, CancellationToken);

        Assert.Equal(expected, passes);
    }

    [Fact]
    public async Task PassesAsync_StubErrors_ReportsFailed()
    {
        await using FakeCoreBankingServer failingStub = await FakeCoreBankingServer.FailingAsync();
        await using ServiceProvider services = CoreBankingServices.Build(failingStub.BaseUrl);

        bool passes = await services.GetRequiredService<ISimSwapCheck>().PassesAsync("+27820000004", CancellationToken);

        Assert.False(passes);
    }

    [Fact]
    public async Task PassesAsync_StubStallsTheBody_ReportsFailedWithinFiveSeconds()
    {
        // The late body would say "no recent swap": the deadline must win.
        await using FakeCoreBankingServer stalledStub = await FakeCoreBankingServer.StalledBodyAsync("{\"recentSimSwap\":false}");
        await using ServiceProvider services = CoreBankingServices.Build(stalledStub.BaseUrl);

        var elapsed = Stopwatch.StartNew();
        bool passes = await services.GetRequiredService<ISimSwapCheck>().PassesAsync("+27820000004", CancellationToken);
        elapsed.Stop();

        Assert.False(passes);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5.5), $"Took {elapsed.Elapsed}.");
    }

    [Fact]
    public async Task PassesAsync_CallerCancels_ThrowsInsteadOfReportingFailed()
    {
        await using FakeCoreBankingServer slowStub = await FakeCoreBankingServer.SlowAsync();
        await using ServiceProvider services = CoreBankingServices.Build(slowStub.BaseUrl);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        caller.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            services.GetRequiredService<ISimSwapCheck>().PassesAsync("+27820000004", caller.Token));
    }

    [Fact]
    public async Task PassesAsync_StubTimesOut_ReportsFailed()
    {
        await using FakeCoreBankingServer slowStub = await FakeCoreBankingServer.SlowAsync();
        await using ServiceProvider services = CoreBankingServices.Build(slowStub.BaseUrl);

        bool passes = await services.GetRequiredService<ISimSwapCheck>().PassesAsync("+27820000004", CancellationToken);

        Assert.False(passes);
    }
}
