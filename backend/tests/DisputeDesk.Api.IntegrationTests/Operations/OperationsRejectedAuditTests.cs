using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// AC6 (FR-25; AD-14a): each action's 404 and 409 writes exactly one <c>Rejected</c> row through <c>AuditLog.Record</c>,
/// with the action, the token's <c>sub</c>, the requested ID for a 404 and the dispute's for a 409, no customer ID and no
/// outcome. A success writes none. A failed save is a 500 with no dispute data (fail closed).
/// </summary>
public sealed class OperationsRejectedAuditTests(OperationsToolFixture api, PostgresFixture postgres) : IClassFixture<OperationsToolFixture>
{
    [Theory]
    [MemberData(nameof(OperationsApi.Actions), MemberType = typeof(OperationsApi))]
    public async Task Action_UnknownId_Returns404AndWritesOneRejectedRowWithTheRequestedId(string action, string role)
    {
        var unknown = Guid.CreateVersion7();

        HttpResponseMessage response = await api.Tool.ActAsync(unknown.ToString(), action, api.Factory.Zitadel.Issue(roles: role), OperationsApi.BodyFor(action));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertRejected(Assert.Single(await api.AuditRowsAsync(unknown)), action, unknown);
    }

    [Theory]
    [InlineData("start-review", DisputeStatus.UnderReview, "underReview")]
    [InlineData("start-review", DisputeStatus.Withdrawn, "withdrawn")]
    [InlineData("escalate", DisputeStatus.Escalated, "escalated")]
    [InlineData("escalate", DisputeStatus.Resolved, "resolved")]
    [InlineData("resolve", DisputeStatus.Submitted, "submitted")]
    [InlineData("resolve", DisputeStatus.Withdrawn, "withdrawn")]
    public async Task Action_StatusThatForbidsIt_Returns409WithDisputeStatusAndWritesOneRejectedRow(string action, DisputeStatus status, string disputeStatus)
    {
        Guid disputeId = await api.DisputeInAsync(status);

        HttpResponseMessage response = await api.Factory.ActAsMappedAsync(api.Tool, disputeId, action);

        await response.AssertNotAllowedInAsync(disputeStatus);
        AssertRejected(Assert.Single(await api.AuditRowsAsync(disputeId)), action, disputeId);
        Assert.Equal(status, (await api.Factory.DisputeRowAsync(disputeId)).Status);
    }

    [Theory]
    [InlineData("start-review", DisputeStatus.Submitted)]
    [InlineData("escalate", DisputeStatus.Submitted)]
    [InlineData("resolve", DisputeStatus.Escalated)]
    public async Task Action_Success_WritesNoAuditRow(string action, DisputeStatus status)
    {
        Guid disputeId = await api.DisputeInAsync(status);

        HttpResponseMessage response = await api.Factory.ActAsMappedAsync(api.Tool, disputeId, action);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await api.AuditRowsAsync(disputeId));
    }

    [Theory]
    [MemberData(nameof(OperationsApi.Actions), MemberType = typeof(OperationsApi))]
    public async Task Action_RejectedSaveFails_Returns500InternalWithNoDisputeData(string action, string role)
    {
        var failAudit = new FailAuditInsertInterceptor { Armed = false };
        await using ApiFactory factory = await Transactions.TransactionsApi.StartAsync(
            postgres, new Uri("http://core-banking.invalid"), api.Now,
            services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failAudit)));
        // Withdrawn: every action is refused with 409.
        Guid disputeId = await factory.RaiseDirectlyAsync("login-a", api.Now.AddHours(-1));
        await factory.AppendAsync(disputeId, DisputeStatus.Withdrawn, null, new DisputeWithdrawn(DisputeStatus.Submitted, DisputeStatus.Withdrawn, null)
        {
            Version = 2,
            OccurredAtUtc = api.Now.AddHours(-1),
            ActorType = ActorType.Customer,
            ActorId = "login-a",
        });
        using HttpClient tool = factory.CreateTool();
        failAudit.Armed = true;

        HttpResponseMessage conflict = await tool.ActAsync(disputeId.ToString(), action, factory.Zitadel.Issue(roles: role), OperationsApi.BodyFor(action));
        HttpResponseMessage notFound = await tool.ActAsync(Guid.CreateVersion7().ToString(), action, factory.Zitadel.Issue(roles: role), OperationsApi.BodyFor(action));

        foreach (HttpResponseMessage response in new[] { conflict, notFound })
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            using JsonDocument problem = await response.ReadJsonAsync();
            Assert.Equal(ProblemType.For(CommonProblems.Internal), problem.RootElement.GetProperty("type").GetString());
            Assert.Equal(["type", "title", "status", "traceId"], OperationsApi.Names(problem.RootElement));
        }

        Assert.Equal(2, failAudit.Failed);
        Assert.Empty(await factory.AuditRowsAsync());
    }

    private void AssertRejected(AuditLogEntry entry, string action, Guid disputeId)
    {
        Assert.Equal(OperationsApi.RoleOf(action) switch
        {
            OperationsApi.StartReviewRole => AuditAction.StartReview,
            OperationsApi.EscalateRole => AuditAction.Escalate,
            _ => AuditAction.Resolve,
        }, entry.Action);
        Assert.Equal(AuditResult.Rejected, entry.Result);
        Assert.Equal(ActorType.Operations, entry.ActorType);
        Assert.Equal(FakeZitadel.OperationsSub, entry.ActorId);
        Assert.Equal(disputeId, entry.DisputeId);
        Assert.Null(entry.CustomerId);
        // The use case's one instant, cut to whole microseconds as stored.
        Assert.Equal(api.Now.AddTicks(-(api.Now.Ticks % TimeSpan.TicksPerMicrosecond)), entry.OccurredAtUtc);
        Assert.Matches("^[0-9a-f]{32}$", entry.TraceId);
    }
}
