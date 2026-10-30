using System.Diagnostics.Metrics;
using System.Net;
using DisputeDesk.Api.Disputes;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// AC8 (AD-20, SM-4, NFR-OBS-3): after each committed operations action only, <c>disputedesk.dispute_actions</c> rises by 1
/// with exactly <c>action</c> and <c>actor_type</c>, and each committed resolve adds 1 to <c>disputedesk.disputes.resolved</c>
/// with exactly <c>outcome</c> and <c>sla_escalated</c>, true only when the SLA sweep escalated the dispute. Refusals,
/// the losing attempt of a clash, a failed audit save and a failed action commit add nothing.
/// </summary>
public class OperationsActionMetricsTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private const string Actions = "disputedesk.dispute_actions";
    private const string Resolved = "disputedesk.disputes.resolved";

    [Fact]
    public async Task Actions_SucceedRetriedRefusedAndClashThenRefused_CountOnlyEachCommitOnce()
    {
        var appendFirst = new AppendNextVersionInterceptor();
        var failAudit = new FailAuditInsertInterceptor { Armed = false };
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(appendFirst, failAudit)));
        using var recorder = new MeasurementRecorder(factory.Services.GetRequiredService<IMeterFactory>());
        using HttpClient tool = factory.CreateTool();
        DateTimeOffset at = stub.NowAsStored.AddHours(-1);
        Guid manual = await factory.RaiseDirectlyAsync("login-a", at, "TXN-A-7001");
        Guid underReview = await factory.RaiseDirectlyAsync("login-a", at, "TXN-A-7002");
        await factory.AppendAsync(underReview, DisputeStatus.UnderReview, null, By(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), 2, at, ActorType.Operations, "ops-a"));
        Guid slaEscalated = await factory.RaiseDirectlyAsync("login-a", at, "TXN-A-7003");
        await factory.AppendAsync(slaEscalated, DisputeStatus.Escalated, null, By(new DisputeEscalated(DisputeStatus.Submitted, DisputeStatus.Escalated), 2, at, ActorType.Automation, Actor.SlaSweepId));
        Guid retried = await factory.RaiseDirectlyAsync("login-a", at, "TXN-A-7004");
        Guid clashThenRefused = await factory.RaiseDirectlyAsync("login-a", at, "TXN-A-7005");

        // Successes: start review, a manual escalate then its resolve, a resolve with no escalation, and one after the sweep's.
        Assert.Equal(HttpStatusCode.OK, (await factory.ActAsMappedAsync(tool, manual, "start-review")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.ActAsMappedAsync(tool, manual, "escalate")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.ActAsMappedAsync(tool, manual, "resolve", OperationsApi.ResolveBody("upheld"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.ActAsMappedAsync(tool, underReview, "resolve", OperationsApi.ResolveBody("upheld"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.ActAsMappedAsync(tool, slaEscalated, "resolve", OperationsApi.ResolveBody("rejected"))).StatusCode);
        // A success on its retry, counted once.
        appendFirst.Arm(By(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), 2, stub.NowAsStored, ActorType.Operations, "ops-b") with { DisputeId = retried }, DisputeStatus.UnderReview);
        Assert.Equal(HttpStatusCode.OK, (await factory.ActAsMappedAsync(tool, retried, "escalate")).StatusCode);
        Assert.Equal(1, appendFirst.Appended);
        // Refusals: a 409, a 404, a 400 and a 403.
        Assert.Equal(HttpStatusCode.Conflict, (await factory.ActAsMappedAsync(tool, manual, "resolve")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.ActAsMappedAsync(tool, Guid.CreateVersion7(), "escalate")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.ActAsMappedAsync(tool, retried, "resolve", """{"outcome":"upheld"}""")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tool.ActAsync(retried.ToString(), "resolve", factory.Zitadel.Issue(), OperationsApi.ResolveBody())).StatusCode);
        // A clash whose reload refuses it.
        appendFirst.Arm(By(new DisputeEscalated(DisputeStatus.Submitted, DisputeStatus.Escalated), 2, stub.NowAsStored, ActorType.Operations, "ops-b") with { DisputeId = clashThenRefused }, DisputeStatus.Escalated);
        Assert.Equal(HttpStatusCode.Conflict, (await factory.ActAsMappedAsync(tool, clashThenRefused, "start-review")).StatusCode);
        // A refusal whose audit save fails.
        failAudit.Armed = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await factory.ActAsMappedAsync(tool, manual, "escalate")).StatusCode);

        Assert.Equal(
            [
                [("action", "startReview"), ("actor_type", "operations")],
                [("action", "escalate"), ("actor_type", "operations")],
                [("action", "resolve"), ("actor_type", "operations")],
                [("action", "resolve"), ("actor_type", "operations")],
                [("action", "resolve"), ("actor_type", "operations")],
                [("action", "escalate"), ("actor_type", "operations")],
            ],
            recorder.Measurements(Actions).Select(measurement => { Assert.Equal(1, measurement.Value); return measurement.Tags; }));
        Assert.Equal(
            [
                [("outcome", "upheld"), ("sla_escalated", "false")],
                [("outcome", "upheld"), ("sla_escalated", "false")],
                [("outcome", "rejected"), ("sla_escalated", "true")],
            ],
            recorder.Measurements(Resolved).Select(measurement => { Assert.Equal(1, measurement.Value); return measurement.Tags; }));
    }

    [Fact]
    public async Task Actions_PermittedButTheCommitFails_Return500AndCountNothing()
    {
        // The outbox insert fails in Postgres inside the action's one commit, so its stored event rolls back with it.
        var failOutbox = new FailOutboxInsertInterceptor { Armed = false };
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failOutbox)));
        using var recorder = new MeasurementRecorder(factory.Services.GetRequiredService<IMeterFactory>());
        using HttpClient tool = factory.CreateTool();
        DateTimeOffset at = stub.NowAsStored.AddHours(-1);
        Guid submitted = await factory.RaiseDirectlyAsync("login-a", at, "TXN-A-7101");
        Guid underReview = await factory.RaiseDirectlyAsync("login-a", at, "TXN-A-7102");
        await factory.AppendAsync(underReview, DisputeStatus.UnderReview, null, By(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), 2, at, ActorType.Operations, "ops-a"));
        failOutbox.Armed = true;

        HttpResponseMessage startReview = await factory.ActAsMappedAsync(tool, submitted, "start-review");
        HttpResponseMessage resolve = await factory.ActAsMappedAsync(tool, underReview, "resolve", OperationsApi.ResolveBody("upheld"));

        Assert.Equal(HttpStatusCode.InternalServerError, startReview.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, resolve.StatusCode);
        Assert.Equal(2, failOutbox.Failed);
        Assert.Empty(recorder.Measurements(Actions));
        Assert.Empty(recorder.Measurements(Resolved));
        Assert.Single(await factory.StreamAsync(submitted));
        Assert.Equal(2, (await factory.StreamAsync(underReview)).Count);
        Assert.Equal(DisputeStatus.Submitted, (await factory.DisputeRowAsync(submitted)).Status);
        Assert.Equal(DisputeStatus.UnderReview, (await factory.DisputeRowAsync(underReview)).Status);
    }

    [Fact]
    public async Task Resolved_Instrument_IsACounter()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using var recorder = new MeasurementRecorder(factory.Services.GetRequiredService<IMeterFactory>());
        factory.Services.GetRequiredService<DisputeActionMetrics>();

        Assert.IsType<Counter<long>>(recorder.Instrument(Resolved));
    }

    private static TEvent By<TEvent>(TEvent storedEvent, int version, DateTimeOffset at, ActorType actorType, string actorId)
        where TEvent : StoredEvent =>
        storedEvent with { Version = version, OccurredAtUtc = at, ActorType = actorType, ActorId = actorId };
}
