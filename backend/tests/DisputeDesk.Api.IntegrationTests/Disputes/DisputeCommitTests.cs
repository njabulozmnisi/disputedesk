using System.Net;
using System.Reflection;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// The one commit path (AD-14): a raise writes its stored event, the read model and the outbox row in one transaction
/// or nothing, writes no audit row, never leaves a failed save behind to be written later, and keeps stored events and
/// audit entries insert only.
/// </summary>
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
    public async Task Raise_Succeeds_WritesOneStoredEventByTheLoginAndNoAuditEntry()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body());

        using JsonDocument body = await response.ReadJsonAsync();
        // One row each in Disputes, StoredEvents and OutboxMessages, and no dispute audit row (ad-009).
        Assert.Equal(RowCounts.One, await factory.CountRowsAsync());
        StoredEventRow stored = await SingleStoredEventAsync(factory);
        Assert.Equal(nameof(DisputeRaised), stored.EventType);
        Assert.Equal(1, stored.Version);
        Assert.Equal(ActorType.Customer, stored.ActorType);
        Assert.Equal("login-a", stored.ActorId);
        Assert.Equal(body.RootElement.GetProperty("disputeId").GetGuid(), stored.DisputeId);
        Assert.Matches("^[0-9a-f]{32}$", stored.TraceId);
        // Data holds only the type's own fields, camelCase; the shared ones are columns (AD-22).
        using var data = JsonDocument.Parse(stored.Data);
        Assert.Equal(
            ["customerId", "disputedAmount", "expectedAmount", "note", "reason", "toStatus", "transaction"],
            data.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(TransactionsApi.CustomerA, data.RootElement.GetProperty("customerId").GetString());
    }

    [Fact]
    public async Task Raise_Succeeds_StoredEventIdIsTheOutboxIdAndOneInstantStampsBoth()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body());

        using JsonDocument body = await response.ReadJsonAsync();
        StoredEventRow stored = await SingleStoredEventAsync(factory);
        OutboxMessage outbox = await SingleOutboxMessageAsync(factory);
        Assert.Equal(stored.Id, outbox.Id);
        // The clock reads a tick finer than Postgres keeps, so these are equal only if the raise stamped one stored instant.
        Assert.Equal(stored.OccurredAtUtc, body.RootElement.GetProperty("createdAt").GetDateTimeOffset());
        // A UUIDv7 whose first 48 bits are the change's instant in Unix milliseconds (RFC 9562, section 5.7).
        Assert.Equal(7, stored.Id.Version);
        Assert.Equal(stored.OccurredAtUtc.ToUnixTimeMilliseconds(), Convert.ToInt64(stored.Id.ToString("N")[..12], 16));
        Assert.Equal(stored.OccurredAtUtc, outbox.CreatedAtUtc);
        using var payload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal(stored.Id.ToString(), payload.RootElement.GetProperty("id").GetString());
        Assert.Equal(stored.OccurredAtUtc, payload.RootElement.GetProperty("time").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveChanges_StoredEventModified_IsRefusedAndTheRowIsUnchanged(bool synchronous)
    {
        string database = await RaisedDatabaseAsync();
        await using AppDbContext db = Context(database);
        StoredEventRow stored = await db.Set<StoredEventRow>().IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
        db.Entry(stored).Property(row => row.ActorId).CurrentValue = "someone-else";

        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(db, synchronous));

        await using AppDbContext check = Context(database);
        Assert.Equal("login-a", (await check.Set<StoredEventRow>().IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken)).ActorId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveChanges_StoredEventDeleted_IsRefusedAndTheRowRemains(bool synchronous)
    {
        string database = await RaisedDatabaseAsync();
        await using AppDbContext db = Context(database);
        db.Remove(await db.Set<StoredEventRow>().IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(db, synchronous));

        Assert.Equal((1, 1, 1), await CountAsync(database));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveChanges_AuditEntryModified_IsRefusedAndTheRowIsUnchanged(bool synchronous)
    {
        string database = await postgres.CreateMigratedDatabaseAsync();
        Guid id = await AddAuditEntryAsync(database);
        await using AppDbContext db = Context(database);
        AuditLogEntry entry = await db.Set<AuditLogEntry>().SingleAsync(TestContext.Current.CancellationToken);
        db.Entry(entry).Property(audit => audit.ActorId).CurrentValue = "someone-else";

        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(db, synchronous));

        await using AppDbContext check = Context(database);
        Assert.Equal("login-a", (await check.Set<AuditLogEntry>().SingleAsync(audit => audit.Id == id, TestContext.Current.CancellationToken)).ActorId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveChanges_AuditEntryDeleted_IsRefusedAndTheRowRemains(bool synchronous)
    {
        string database = await postgres.CreateMigratedDatabaseAsync();
        await AddAuditEntryAsync(database);
        await using AppDbContext db = Context(database);
        db.Remove(await db.Set<AuditLogEntry>().SingleAsync(TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(db, synchronous));

        await using AppDbContext check = Context(database);
        Assert.Equal(1, await check.Set<AuditLogEntry>().CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveChanges_AddedDisputeWithNoPendingStoredEvent_IsRefusedAndWritesNothing(bool synchronous)
    {
        string database = await postgres.CreateMigratedDatabaseAsync();
        await using AppDbContext db = Context(database);
        Dispute dispute = RaisedDispute();
        dispute.TakePendingChanges();
        db.Set<Dispute>().Add(dispute);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(db, synchronous));

        Assert.Equal((0, 0, 0), await CountAsync(database));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveChanges_ModifiedDisputeWithNoPendingStoredEvent_IsRefusedAndTheRowIsUnchanged(bool synchronous)
    {
        string database = await RaisedDatabaseAsync();
        await using AppDbContext db = Context(database);
        Dispute dispute = await db.Set<Dispute>().IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
        db.Entry(dispute).Property(row => row.Status).CurrentValue = DisputeStatus.Withdrawn;

        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(db, synchronous));

        await using AppDbContext check = Context(database);
        Assert.Equal(DisputeStatus.Submitted, (await check.Set<Dispute>().IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal((1, 1, 1), await CountAsync(database));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveChanges_AgainAfterLosingToTheUniqueIndex_WritesNothingMore(bool synchronous)
    {
        string database = await RaisedDatabaseAsync();
        await using AppDbContext loser = Context(database);
        loser.Set<Dispute>().Add(RaisedDispute());

        UniqueViolationException violation = await Assert.ThrowsAsync<UniqueViolationException>(() => SaveAsync(loser, synchronous));

        Assert.Equal(DisputeErrors.AlreadyDisputed, violation.Error);
        Assert.Empty(loser.ChangeTracker.Entries());
        Assert.Equal(0, await SaveAsync(loser, synchronous));
        Assert.Equal((1, 1, 1), await CountAsync(database));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveChanges_AgainAfterAFailedInsert_WritesNothingAndRethrowsTheFailureUnchanged(bool synchronous)
    {
        string database = await postgres.CreateMigratedDatabaseAsync();
        var failOutbox = new FailOutboxInsertInterceptor();
        await using AppDbContext db = Context(database, failOutbox);
        db.Set<Dispute>().Add(RaisedDispute());

        Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => SaveAsync(db, synchronous));

        Assert.Equal(1, failOutbox.Failed);
        Assert.IsType<DbUpdateException>(failure);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(0, await SaveAsync(db, synchronous));
        Assert.Equal((0, 0, 0), await CountAsync(database));
    }

    [Fact]
    public async Task SaveChangesAsync_AgainAfterACancelledSave_WritesNothing()
    {
        string database = await postgres.CreateMigratedDatabaseAsync();
        await using AppDbContext db = Context(database);
        db.Set<Dispute>().Add(RaisedDispute());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.SaveChangesAsync(new CancellationToken(canceled: true)));

        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(0, await db.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal((0, 0, 0), await CountAsync(database));
    }

    [Fact]
    public async Task SaveChanges_PendingTypeTheMappingDoesNotName_ThrowsAndWritesNothing()
    {
        string database = await postgres.CreateMigratedDatabaseAsync();
        await using AppDbContext db = Context(database);
        Dispute dispute = RaisedDispute();
        // Only the aggregate appends stored events; reflection stages a type no Domain method can append.
        List<StoredEvent> pending = (List<StoredEvent>?)typeof(Dispute).GetField("_pendingChanges", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(dispute)
            ?? throw new InvalidOperationException("Dispute._pendingChanges not found.");
        pending.Add(new UnmappedStoredEvent { DisputeId = dispute.Id, Version = 2, OccurredAtUtc = stub.Now, ActorId = "login-a" });
        db.Set<Dispute>().Add(dispute);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(0, await db.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal((0, 0, 0), await CountAsync(database));
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

    /// <summary>A fresh database holding one committed raise.</summary>
    private async Task<string> RaisedDatabaseAsync()
    {
        string database = await postgres.CreateMigratedDatabaseAsync();
        await using AppDbContext db = Context(database);
        db.Set<Dispute>().Add(RaisedDispute());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return database;
    }

    private async Task<Guid> AddAuditEntryAsync(string database)
    {
        await using AppDbContext db = Context(database);
        var entry = new AuditLogEntry
        {
            Id = Guid.CreateVersion7(stub.Now),
            OccurredAtUtc = stub.Now,
            Action = AuditAction.LinkingStart,
            Result = AuditResult.Succeeded,
            ActorType = ActorType.Customer,
            ActorId = "login-a",
        };
        db.Set<AuditLogEntry>().Add(entry);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return entry.Id;
    }

    private static AppDbContext Context(string database, FailOutboxInsertInterceptor? interceptor = null)
    {
        DbContextOptionsBuilder<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database);
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return new AppDbContext(options.Options);
    }

    // Both commit paths, SaveChanges and SaveChangesAsync, behind one call for the theories.
    private static Task<int> SaveAsync(AppDbContext db, bool synchronous) =>
        synchronous ? Task.FromResult(db.SaveChanges()) : db.SaveChangesAsync(TestContext.Current.CancellationToken);

    /// <summary>The rows in <c>Disputes</c>, <c>StoredEvents</c> and <c>OutboxMessages</c>.</summary>
    private static async Task<(int Disputes, int StoredEvents, int OutboxMessages)> CountAsync(string database)
    {
        await using AppDbContext db = Context(database);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        return (
            await db.Set<Dispute>().IgnoreQueryFilters().CountAsync(cancellationToken),
            await db.Set<StoredEventRow>().IgnoreQueryFilters().CountAsync(cancellationToken),
            await db.Set<OutboxMessage>().CountAsync(cancellationToken));
    }

    private static async Task<StoredEventRow> SingleStoredEventAsync(ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<StoredEventRow>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<OutboxMessage> SingleOutboxMessageAsync(ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<OutboxMessage>().AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A stored event type the publish mapping does not name.</summary>
    private sealed record UnmappedStoredEvent : StoredEvent;
}
