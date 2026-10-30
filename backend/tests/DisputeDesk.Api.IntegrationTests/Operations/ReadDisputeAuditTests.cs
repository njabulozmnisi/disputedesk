using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// AC6, FR-25 and NFR-SEC-10: every operations read is audited through <c>AuditLog.Record</c>, <c>Succeeded</c> for a
/// dispute and <c>Rejected</c> for an unknown ID, with the token's <c>sub</c> and no customer ID. The entry is saved before
/// the body is returned, so a failed save is a 500 with no dispute data (fail closed; OWASP Logging Cheat Sheet).
/// A 401, 403 or 400 writes nothing here.
/// </summary>
public class ReadDisputeAuditTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task ReadDispute_Found_WritesOneSucceededEntryWithTheOperationsActorAndNoCustomerId()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();

        HttpResponseMessage response = await tool.ReadDisputeAsync(disputeId.ToString(), factory.Zitadel.Issue(roles: OperationsApi.ReadRole));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AuditLogEntry entry = Assert.Single(await factory.DisputeReadEntriesAsync(disputeId));
        AssertOperationsEntry(entry, AuditResult.Succeeded, disputeId);
        // The entry is stamped with the use case's instant, read from the injected clock (AD-4).
        Assert.Equal(stub.NowAsStored, entry.OccurredAtUtc);
    }

    [Fact]
    public async Task ReadDispute_UnknownId_WritesOneRejectedEntryWithTheRequestedId()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient tool = factory.CreateTool();
        var unknown = Guid.NewGuid();

        HttpResponseMessage response = await tool.ReadDisputeAsync(unknown.ToString(), factory.Zitadel.Issue(roles: OperationsApi.ReadRole));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertOperationsEntry(Assert.Single(await factory.DisputeReadEntriesAsync(unknown)), AuditResult.Rejected, unknown);
    }

    [Fact]
    public async Task ReadDispute_AuditSaveFails_Returns500WithNoDisputeDataAndNoEntry()
    {
        var failAudit = new FailAuditInsertInterceptor { Armed = false };
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failAudit)));
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();
        failAudit.Armed = true;

        HttpResponseMessage response = await tool.ReadDisputeAsync(disputeId.ToString(), factory.Zitadel.Issue(roles: OperationsApi.ReadRole));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, failAudit.Failed);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(CommonProblems.Internal), problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(["type", "title", "status", "traceId"], OperationsApi.Names(problem.RootElement));
        Assert.Empty(await factory.DisputeReadEntriesAsync(disputeId));
    }

    [Fact]
    public async Task ReadDispute_Refused401403Or400_WritesNoEntry()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();

        Assert.Equal(HttpStatusCode.Unauthorized, (await tool.ReadDisputeAsync(disputeId.ToString(), token: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tool.ReadDisputeAsync(disputeId.ToString(), factory.Zitadel.Issue())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await tool.ReadDisputeAsync("not-a-guid", factory.Zitadel.Issue(roles: OperationsApi.ReadRole))).StatusCode);

        // The database is this test's own, so every DisputeRead entry in it would be one of these.
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditLogEntry>()
            .AnyAsync(entry => entry.Action == AuditAction.DisputeRead, TestContext.Current.CancellationToken));
    }

    private static void AssertOperationsEntry(AuditLogEntry entry, AuditResult result, Guid disputeId)
    {
        Assert.Equal(AuditAction.DisputeRead, entry.Action);
        Assert.Equal(result, entry.Result);
        Assert.Equal(ActorType.Operations, entry.ActorType);
        Assert.Equal(FakeZitadel.OperationsSub, entry.ActorId);
        Assert.Equal(disputeId, entry.DisputeId);
        Assert.Null(entry.CustomerId);
    }
}
