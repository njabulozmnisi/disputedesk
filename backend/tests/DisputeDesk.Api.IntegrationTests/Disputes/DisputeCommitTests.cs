using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>AC6: the one commit path (AD-14) writes all four rows or none, and keeps the audit log append-only.</summary>
public class DisputeCommitTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task Raise_OutboxInsertFailsInTheDatabase_RollsBackEveryRowAndReturns500()
    {
        var failOutbox = new FailOutboxInsertInterceptor();
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failOutbox)));
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body());

        Assert.Equal(1, failOutbox.Failed);
        Assert.True(failOutbox.DisputesInsertedFirst);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(CommonProblems.Internal), problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(RowCounts.None, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Raise_Succeeds_WritesTheAuditEntryWithTheLoginAsActorAndTheCustomerApart()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body());

        using JsonDocument body = await response.ReadJsonAsync();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AuditLogEntry audit = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditLogEntry>()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AuditAction.Raise, audit.Action);
        Assert.Equal(AuditResult.Succeeded, audit.Result);
        Assert.Equal(ActorType.Customer, audit.ActorType);
        Assert.Equal("login-a", audit.ActorId);
        Assert.Equal(TransactionsApi.CustomerA, audit.CustomerId);
        Assert.Equal(body.RootElement.GetProperty("disputeId").GetGuid(), audit.DisputeId);
        Assert.Matches("^[0-9a-f]{32}$", audit.TraceId);
    }

    [Fact]
    public async Task SaveChanges_AuditEntryModified_IsRefusedAndTheRowIsUnchanged()
    {
        await using ApiFactory factory = await RaisedAsync();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        AuditLogEntry audit = await db.Set<AuditLogEntry>().SingleAsync(TestContext.Current.CancellationToken);
        db.Entry(audit).Property(entry => entry.ActorId).CurrentValue = "someone-else";

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal("login-a", await ActorIdInTheDatabaseAsync(factory));
    }

    [Fact]
    public async Task SaveChanges_AuditEntryDeleted_IsRefusedAndTheRowRemains()
    {
        await using ApiFactory factory = await RaisedAsync();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<AuditLogEntry>().Remove(await db.Set<AuditLogEntry>().SingleAsync(TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(RowCounts.One, await factory.CountRowsAsync());
    }

    [Fact]
    public void SaveChangesSynchronous_AuditEntryModified_IsRefusedToo()
    {
        using AppDbContext db = postgres.CreateDbContext();
        AuditLogEntry entry = new()
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = stub.Now,
            Action = AuditAction.Raise,
            Result = AuditResult.Succeeded,
            ActorType = ActorType.Customer,
            ActorId = "login-a",
        };
        db.Attach(entry);
        db.Entry(entry).State = EntityState.Modified;

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task SaveChanges_AgainAfterLosingToTheUniqueIndex_WritesNothingMore()
    {
        // After a lost race the context tracks nothing (ChangeTracker.Clear), so a later save on it cannot insert the
        // losing dispute and its rows a second time.
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string database = await postgres.CreateMigratedDatabaseAsync();
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options;
        await using (var winner = new AppDbContext(options))
        {
            winner.Set<Dispute>().Add(RaisedDispute());
            await winner.SaveChangesAsync(cancellationToken);
        }

        await using var loser = new AppDbContext(options);
        loser.Set<Dispute>().Add(RaisedDispute());
        await Assert.ThrowsAsync<UniqueViolationException>(() => loser.SaveChangesAsync(cancellationToken));

        Assert.Equal(0, await loser.SaveChangesAsync(cancellationToken));
        Assert.Empty(loser.ChangeTracker.Entries());
        Assert.Equal(1, await loser.Set<Dispute>().IgnoreQueryFilters().CountAsync(cancellationToken));
        Assert.Equal(1, await loser.Set<OutboxMessage>().CountAsync(cancellationToken));
        Assert.Equal(1, await loser.Set<AuditLogEntry>().CountAsync(cancellationToken));
    }

    private Dispute RaisedDispute()
    {
        var transaction = new TransactionSnapshot(
            "ACC-A-1", DisputesApi.Disputable, "0011", stub.Now.AddDays(-2), "Steers Braamfontein", 87.23m, "ZAR", null,
            TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);
        return Dispute.Raise(
            TransactionsApi.CustomerA, transaction, everDisputed: false, Reason.Unrecognised, note: null, expectedAmount: null,
            new Actor(ActorType.Customer, "login-a"), stub.Now).Value;
    }

    private async Task<ApiFactory> RaisedAsync()
    {
        ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Assert.Equal(HttpStatusCode.Created, (await browser.RaiseAsync(DisputesApi.Body())).StatusCode);
        return factory;
    }

    private static async Task<string> ActorIdInTheDatabaseAsync(ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditLogEntry>()
            .Select(entry => entry.ActorId).SingleAsync(TestContext.Current.CancellationToken);
    }
}
