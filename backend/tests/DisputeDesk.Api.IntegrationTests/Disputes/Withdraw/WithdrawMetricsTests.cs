using System.Diagnostics.Metrics;
using System.Net;
using DisputeDesk.Api.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace DisputeDesk.Api.IntegrationTests.Disputes.Withdraw;

/// <summary>
/// AC7 (AD-20, SM-C3): <c>disputedesk.dispute_actions</c> counts each successful withdraw, the retried one included, by
/// <c>action</c> and <c>actor_type</c> only, and <c>disputedesk.disputes.withdraw_age</c> records seconds from raise to
/// withdraw with no labels. Each <see cref="MetricCollector{T}"/> reads only this test's own Api's meters.
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
        IMeterFactory meterFactory = factory.Services.GetRequiredService<IMeterFactory>();
        using var actions = new MetricCollector<long>(meterFactory, "DisputeDesk", Actions);
        using var withdrawAge = new MetricCollector<double>(meterFactory, "DisputeDesk", WithdrawAge);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        // Both raised 30 minutes before the Api's clock, straight through the commit path.
        DateTimeOffset raisedAt = stub.NowAsStored.AddMinutes(-30);
        Guid plain = (await factory.DisputeInAsync(DisputeStatus.Submitted, transactionId: "TXN-A-9001", raisedAt: raisedAt)).Id;
        Guid retried = (await factory.DisputeInAsync(DisputeStatus.Submitted, transactionId: "TXN-A-9002", raisedAt: raisedAt)).Id;

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
            actions.GetMeasurementSnapshot().Select(measurement => { Assert.Equal(1, measurement.Value); return measurement.SortedTags(); }));
        Assert.Equal([1800d, 1800d], withdrawAge.GetMeasurementSnapshot().Select(measurement => measurement.Value));
        Assert.All(withdrawAge.GetMeasurementSnapshot(), measurement => Assert.Empty(measurement.Tags));
    }

    [Fact]
    public async Task WithdrawAge_Instrument_IsAHistogramInSecondsWithTheAdvisedBuckets()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        IMeterFactory meterFactory = factory.Services.GetRequiredService<IMeterFactory>();
        using var actions = new MetricCollector<long>(meterFactory, "DisputeDesk", Actions);
        using var withdrawAge = new MetricCollector<double>(meterFactory, "DisputeDesk", WithdrawAge);
        factory.Services.GetRequiredService<DisputeActionMetrics>();

        Histogram<double> histogram = Assert.IsType<Histogram<double>>(withdrawAge.Instrument);

        Assert.Equal("s", histogram.Unit);
        Assert.Equal([60d, 300, 900, 1800, 3600, 7200, 21600, 86400, 604800, 2592000], histogram.Advice?.HistogramBucketBoundaries);
        Assert.IsType<Counter<long>>(actions.Instrument);
    }
}
