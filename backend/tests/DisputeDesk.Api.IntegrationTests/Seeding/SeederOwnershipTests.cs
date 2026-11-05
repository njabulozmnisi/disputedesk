using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.IntegrationTests.Seeding;

/// <summary>
/// Story 8.15: the seeder reads fixture disputes past the ownership filter (AD-11's third named bypass), so its match
/// names the fixture dispute's customer as well as its transaction; the bypass never makes another customer's row count.
/// </summary>
public class SeederOwnershipTests(PostgresFixture postgres)
{
    private const string OtherCustomer = "CUS-000099";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Seed_AnotherCustomersDisputeOnAFixtureTransaction_FailsOnTheUniqueIndexInsteadOfSkipping(bool synchronous)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.CreateMigratedDatabaseAsync();
        var seeder = new Seeder(
            Options.Create(new SeedOptions { Enabled = true, FixturesPath = FixturesFile.FindPath() }), new TestClock(), NullLogger<Seeder>.Instance);
        await using (AppDbContext first = SeededApi.Open(connectionString))
        {
            await seeder.SeedAsync(first, cancellationToken);
        }

        // The first fixture dispute's row now belongs to another customer, on the same account and transaction.
        JsonElement fixture = SeededApi.FixtureDisputes()[0];
        string accountRef = fixture.GetProperty("accountRef").GetString() ?? throw new InvalidOperationException("No accountRef.");
        string transactionId = fixture.GetProperty("transactionId").GetString() ?? throw new InvalidOperationException("No transactionId.");
        await using AppDbContext db = SeededApi.Open(connectionString);
        int moved = await db.Set<Dispute>().IgnoreQueryFilters()
            .Where(dispute => dispute.AccountRef == accountRef && dispute.TransactionId == transactionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(dispute => dispute.CustomerId, OtherCustomer), cancellationToken);
        Assert.Equal(1, moved);

        UniqueViolationException violation = synchronous
            ? Assert.Throws<UniqueViolationException>(() => seeder.Seed(db))
            : await Assert.ThrowsAsync<UniqueViolationException>(() => seeder.SeedAsync(db, cancellationToken));

        Assert.Equal(DisputeErrors.AlreadyDisputed, violation.Error);
    }
}
