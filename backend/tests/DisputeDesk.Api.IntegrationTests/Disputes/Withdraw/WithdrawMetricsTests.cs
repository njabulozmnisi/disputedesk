using System.Diagnostics.Metrics;
using System.Net;
using DisputeDesk.Api.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes.Withdraw;

/// <summary>
/// AC7 (AD-20, SM-C3): <c>disputedesk.dispute_actions</c> counts each successful withdraw, the retried one included, by
/// <c>action</c> and <c>actor_type</c> only, and <c>disputedesk.disputes.withdraw_age</c> records seconds from raise to
/// withdraw with no labels. A <see cref="MeterListener"/> listens only to this test's own Api's meters.
/// </summary>
public class WithdrawMetricsTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private const string Actions = "disputedesk.dispute_actions";
    private const string WithdrawAge = "disputedesk.disputes.withdraw_age";

    [Fact]
    public async Task Withdraw_SucceedsRetriedAndRefused_CountsEachSuccessOnceAndRecordsItsAgeInSeconds()
    {
        var appendFirst = new AppendNextVersionInterceptor();
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(appendFirst)));
        using var recorder = new MeasurementRecorder(factory.Services.GetRequiredService<IMeterFactory>());
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        // Both raised 30 minutes before the Api's clock, straight through the commit path.
        DateTimeOffset raisedAt = stub.NowAsStored.AddMinutes(-30);
        Guid plain = await factory.RaiseDirectlyAsync("login-a", raisedAt, "TXN-A-9001");
        Guid retried = await factory.RaiseDirectlyAsync("login-a", raisedAt, "TXN-A-9002");

        Assert.Equal(HttpStatusCode.OK, (await browser.WithdrawAsync(plain.ToString())).StatusCode);
        appendFirst.Arm(
            new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview)
            {
                DisputeId = retried,
                Version = 2,
                OccurredAtUtc = stub.NowAsStored,
                ActorType = ActorType.Operations,
                ActorId = "ops-a",
            },
            DisputeStatus.UnderReview);
        Assert.Equal(HttpStatusCode.OK, (await browser.WithdrawAsync(retried.ToString())).StatusCode);
        Assert.Equal(1, appendFirst.Appended);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.WithdrawAsync(plain.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.WithdrawAsync(Guid.CreateVersion7().ToString())).StatusCode);

        Assert.Equal(
            [[("action", "withdraw"), ("actor_type", "customer")], [("action", "withdraw"), ("actor_type", "customer")]],
            recorder.Measurements(Actions).Select(measurement => { Assert.Equal(1, measurement.Value); return measurement.Tags; }));
        Assert.Equal([1800d, 1800d], recorder.Measurements(WithdrawAge).Select(measurement => measurement.Value));
        Assert.All(recorder.Measurements(WithdrawAge), measurement => Assert.Empty(measurement.Tags));
    }

    [Fact]
    public async Task WithdrawAge_Instrument_IsAHistogramInSecondsWithTheAdvisedBuckets()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using var recorder = new MeasurementRecorder(factory.Services.GetRequiredService<IMeterFactory>());
        factory.Services.GetRequiredService<DisputeActionMetrics>();

        Histogram<double> histogram = Assert.IsType<Histogram<double>>(recorder.Instrument(WithdrawAge));

        Assert.Equal("s", histogram.Unit);
        Assert.Equal([60d, 300, 900, 1800, 3600, 7200, 21600, 86400, 604800, 2592000], histogram.Advice?.HistogramBucketBoundaries);
        Assert.IsType<Counter<long>>(recorder.Instrument(Actions));
    }
}
