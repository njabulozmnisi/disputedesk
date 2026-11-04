using System.Diagnostics.Metrics;
using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AC11: <c>disputedesk.disputes.raised</c> by <c>reason</c> and <c>disputedesk.raise.rejections</c> by <c>status</c>.
/// Each <see cref="MetricCollector{T}"/> reads only meters of this test's own Api (its <see cref="IMeterFactory"/>), so
/// raises in tests running alongside are never counted here.
/// </summary>
public class RaiseMetricsTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private const string Raised = "disputedesk.disputes.raised";
    private const string Rejections = "disputedesk.raise.rejections";

    [Fact]
    public async Task Raise_SucceedsThenRefused409And422_CountsRaisedByReasonAndRejectionsByStatus()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using MetricCollector<long> raised = Collector(factory, Raised);
        using MetricCollector<long> rejections = Collector(factory, Rejections);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        await browser.RaiseAsync(DisputesApi.Body(reason: "duplicateCharge"));
        await browser.RaiseAsync(DisputesApi.Body());                         // 409, already disputed
        await browser.RaiseAsync(DisputesApi.Body(DisputesApi.Pending));      // 422, not disputable
        await browser.RaiseAsync(DisputesApi.Body("TXN-A-9999"));             // 404, not a rejection

        Assert.Equal([("reason", "duplicateCharge")], Tags(raised));
        Assert.Equal([("status", "409"), ("status", "422")], Tags(rejections).Order());
    }

    [Fact]
    public async Task Raise_IdempotencyMismatchAnsweredTwice_CountsTwo422Rejections()
    {
        // 7.13 AC8: a mismatch is not stored, so each one is a new answer the customer receives.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using MetricCollector<long> rejections = Collector(factory, Rejections);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string key = DisputesApi.NewKey();
        Assert.Equal(HttpStatusCode.Created, (await browser.RaiseWithKeyAsync(DisputesApi.Body(), key)).StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.RaiseWithKeyAsync(DisputesApi.Body(reason: "duplicateCharge"), key)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.RaiseWithKeyAsync(DisputesApi.Body(reason: "duplicateCharge"), key)).StatusCode);

        Assert.Equal([("status", "422"), ("status", "422")], Tags(rejections));
    }

    [Fact]
    public async Task Raise_IdempotencyInProgressAnsweredTwice_CountsTwo409Rejections()
    {
        var hold = new HoldInterceptor("FROM \"Disputes\"");
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(hold)));
        using MetricCollector<long> rejections = Collector(factory, Rejections);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        string token = await browser.AntiforgeryTokenAsync();
        string key = DisputesApi.NewKey();
        Task<HttpResponseMessage> first = browser.RaiseAsync(DisputesApi.Body(), token, key);
        await hold.Reached.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, (await browser.RaiseAsync(DisputesApi.Body(), token, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.RaiseAsync(DisputesApi.Body(), token, key)).StatusCode);
        hold.Release();

        Assert.Equal(HttpStatusCode.Created, (await first).StatusCode);
        Assert.Equal([("status", "409"), ("status", "409")], Tags(rejections));
    }

    [Fact]
    public async Task Raise_StoredRejectionsReplayed_CountsEachRejectionOnlyWhenFirstAnswered()
    {
        // A replay is the same rejection, counted when it was first answered (NFR-OBS-3).
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using MetricCollector<long> rejections = Collector(factory, Rejections);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        await browser.RaiseCreatedAsync(DisputesApi.Body());
        string alreadyDisputedKey = DisputesApi.NewKey();
        string notDisputableKey = DisputesApi.NewKey();
        Assert.Equal(HttpStatusCode.Conflict, (await browser.RaiseWithKeyAsync(DisputesApi.Body(), alreadyDisputedKey)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.RaiseWithKeyAsync(DisputesApi.Body(DisputesApi.Pending), notDisputableKey)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await browser.RaiseWithKeyAsync(DisputesApi.Body(), alreadyDisputedKey)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await browser.RaiseWithKeyAsync(DisputesApi.Body(DisputesApi.Pending), notDisputableKey)).StatusCode);

        Assert.Equal([("status", "409"), ("status", "422")], Tags(rejections).Order());
    }

    [Fact]
    public async Task Raise_AnyOutcome_LabelsCarryNoPersonalData()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using MetricCollector<long> raised = Collector(factory, Raised);
        using MetricCollector<long> rejections = Collector(factory, Rejections);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        await browser.RaiseAsync(DisputesApi.Body(note: "note-marker"));
        await browser.RaiseAsync(DisputesApi.Body());

        Assert.All(
            raised.GetMeasurementSnapshot().Concat(rejections.GetMeasurementSnapshot()).SelectMany(measurement => measurement.Tags.Keys),
            name => Assert.True(name is "reason" or "status", name));
    }

    private static MetricCollector<long> Collector(ApiFactory factory, string instrument) =>
        new(factory.Services.GetRequiredService<IMeterFactory>(), "DisputeDesk", instrument);

    /// <summary>The one tag of each measurement, once per count it added.</summary>
    private static List<(string Name, string Value)> Tags(MetricCollector<long> collector) =>
        [.. collector.GetMeasurementSnapshot().SelectMany(measurement => Enumerable.Repeat(Assert.Single(measurement.SortedTags()), (int)measurement.Value))];
}
