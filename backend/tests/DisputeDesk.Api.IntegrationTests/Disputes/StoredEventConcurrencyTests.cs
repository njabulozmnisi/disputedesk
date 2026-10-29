using System.Reflection;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AD-15: <c>UQ_StoredEvents_DisputeId_Version</c> is the one concurrency check. A save that appends a version another
/// save already appended loses as a version clash, and nothing of it commits, its <c>Disputes</c> change and outbox
/// row included.
/// </summary>
public class StoredEventConcurrencyTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SaveChanges_SecondAppendOfTheSameVersion_IsTheVersionClashAndCommitsNothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string database = await postgres.CreateMigratedDatabaseAsync();
        await using (AppDbContext winner = Context(database))
        {
            winner.Set<Dispute>().Add(RaisedDispute());
            await winner.SaveChangesAsync(cancellationToken);
        }

        // No method appends a second stored event before story 3.4, so reflection stages the losing change: the dispute's
        // read model changed, and a pending stored event at the version the winner already holds, which the commit turns
        // into its stored event and outbox rows.
        await using AppDbContext loser = Context(database);
        Dispute dispute = await loser.Set<Dispute>().IgnoreQueryFilters().SingleAsync(cancellationToken);
        loser.Entry(dispute).Property(row => row.Status).CurrentValue = DisputeStatus.UnderReview;
        List<StoredEvent> pending = (List<StoredEvent>?)typeof(Dispute).GetField("_pendingChanges", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(dispute)
            ?? throw new InvalidOperationException("Dispute._pendingChanges not found.");
        pending.Add(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview)
        {
            DisputeId = dispute.Id,
            Version = 1,
            OccurredAtUtc = Now.AddHours(1),
            ActorType = ActorType.Operations,
            ActorId = "ops-a",
        });

        UniqueViolationException violation = await Assert.ThrowsAsync<UniqueViolationException>(() => loser.SaveChangesAsync(cancellationToken));

        Assert.Equal(DisputeErrors.VersionClash, violation.Error);
        await using AppDbContext check = Context(database);
        Assert.Equal(DisputeStatus.Submitted, (await check.Set<Dispute>().IgnoreQueryFilters().SingleAsync(cancellationToken)).Status);
        Assert.Equal(1, await check.Set<StoredEventRow>().IgnoreQueryFilters().CountAsync(cancellationToken));
        Assert.Equal(1, await check.Set<OutboxMessage>().CountAsync(cancellationToken));
    }

    private static AppDbContext Context(string database) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options);

    private static Dispute RaisedDispute() =>
        Dispute.Raise(
            TransactionsApi.CustomerA,
            new TransactionSnapshot("ACC-A-1", "TXN-A-0029", "0011", Now.AddDays(-2), "Shop", 10m, "ZAR", null,
                TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled),
            everDisputed: false,
            Reason.Unrecognised,
            note: null,
            expectedAmount: null,
            new Actor(ActorType.Customer, "login-a"),
            Now).Value;
}
