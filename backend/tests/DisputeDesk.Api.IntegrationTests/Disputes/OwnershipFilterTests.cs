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
/// AC9, on real Postgres: the <c>Ownership</c> filter (AD-11) reads the customer of each context instance, matches
/// nothing without one, and reaches stored events through their dispute.
/// </summary>
public sealed class OwnershipFilterTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private string _database = "";

    public async ValueTask InitializeAsync()
    {
        _database = await postgres.CreateMigratedDatabaseAsync();
        await using AppDbContext db = Context(customerId: null);
        db.Set<Dispute>().Add(Raise(TransactionsApi.CustomerA, "ACC-A-1", "TXN-A-0029"));
        db.Set<Dispute>().Add(Raise(TransactionsApi.CustomerB, "ACC-B-1", "TXN-B-0027"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Disputes_NoCurrentCustomer_MatchNothingAndDoNotThrow()
    {
        await using AppDbContext db = Context(customerId: null);

        Assert.Empty(await db.Set<Dispute>().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.Set<StoredEventRow>().ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Disputes_ContextWithoutCurrentCustomer_MatchNothing()
    {
        // The Worker's context: no CurrentCustomer at all.
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database).Options);

        Assert.Equal(0, await db.Set<Dispute>().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await db.Set<Dispute>().IgnoreQueryFilters([AppDbContext.OwnershipFilter]).CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Disputes_TwoContextsForTwoCustomers_EachSeesOnlyItsOwn()
    {
        // The same compiled model and query, two context instances: the filter is not baked to the first customer.
        await using AppDbContext forA = Context(TransactionsApi.CustomerA);
        await using AppDbContext forB = Context(TransactionsApi.CustomerB);

        Assert.Equal(["TXN-A-0029"], await TransactionIdsAsync(forA));
        Assert.Equal(["TXN-B-0027"], await TransactionIdsAsync(forB));
        Assert.Equal(["TXN-A-0029"], await TransactionIdsAsync(forA));
    }

    [Fact]
    public async Task StoredEvents_QueriedDirectly_AreFilteredThroughTheirDispute()
    {
        await using AppDbContext forA = Context(TransactionsApi.CustomerA);
        await using AppDbContext forB = Context(TransactionsApi.CustomerB);

        StoredEventRow forAOnly = Assert.Single(await forA.Set<StoredEventRow>().ToListAsync(TestContext.Current.CancellationToken));
        StoredEventRow forBOnly = Assert.Single(await forB.Set<StoredEventRow>().ToListAsync(TestContext.Current.CancellationToken));

        Assert.Equal("login-CUS-000001", forAOnly.ActorId);
        Assert.Equal("login-CUS-000002", forBOnly.ActorId);
        Assert.Equal(2, await forA.Set<StoredEventRow>().IgnoreQueryFilters([AppDbContext.OwnershipFilter]).CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Disputes_CustomerResolvedAfterTheContextWasCreated_IsUsedAtQueryTime()
    {
        // As in a request: the context exists before the Linked policy resolves the customer.
        var currentCustomer = new CurrentCustomer();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database).Options, currentCustomer);
        Assert.Empty(await TransactionIdsAsync(db));

        currentCustomer.Set(TransactionsApi.CustomerB, "login-b");

        Assert.Equal(["TXN-B-0027"], await TransactionIdsAsync(db));
    }

    private AppDbContext Context(string? customerId)
    {
        var currentCustomer = new CurrentCustomer();
        if (customerId is not null)
        {
            currentCustomer.Set(customerId, "login-" + customerId);
        }

        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database).Options, currentCustomer);
    }

    private static Task<List<string>> TransactionIdsAsync(AppDbContext db) =>
        db.Set<Dispute>().Select(dispute => dispute.TransactionId).ToListAsync(TestContext.Current.CancellationToken);

    private static Dispute Raise(string customerId, string accountRef, string transactionId) =>
        Dispute.Raise(
            customerId,
            new TransactionSnapshot(accountRef, transactionId, "0011", Now.AddDays(-2), "Shop", 10m, "ZAR", null,
                TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled),
            everDisputed: false,
            Reason.Unrecognised,
            note: null,
            expectedAmount: null,
            new Actor(ActorType.Customer, "login-" + customerId),
            Now).Value;
}
