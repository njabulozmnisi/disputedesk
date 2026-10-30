extern alias CoreBankingStub;

using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoreBankingStub::DisputeDesk.CoreBankingStub;
using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Ownership;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Json;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.IntegrationTests.Seeding;

/// <summary>
/// Story 5.4: the seeder writes the fixture disputes through the <see cref="Dispute"/> aggregate and the AD-14 commit,
/// each test on its own empty database and a clock that reads a tick finer than Postgres keeps.
/// </summary>
public class SeededDisputesTests(PostgresFixture postgres)
{
    private const string CustomerA = "CUS-000001";
    private const string CustomerB = "CUS-000002";
    private const string LoginA = "dd-login-customer-a";
    private const string LoginB = "dd-login-customer-b";
    private const string SlaDemoTransaction = "TXN-A-0011";

    // South Africa has no daylight saving time, so its offset is always UTC+02:00 (AD-4).
    private static readonly TimeSpan SouthAfricanOffset = TimeSpan.FromHours(2);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seed_EmptyDatabase_GivesAADisputeInEveryStatusAndOutcomeThatReplaysToItsRowAndTimeline()
    {
        var clock = new TestClock();
        (ApiFactory factory, string connectionString) = await SeededApi.StartAsync(postgres, clock);
        await using ApiFactory _ = factory;
        using HttpClient a = factory.CreateBrowser(new CookieContainer());
        await a.SignInAsync(factory.Zitadel, LoginA);
        await using AppDbContext db = SeededApi.Open(connectionString);
        List<Dispute> disputes = await db.Set<Dispute>().IgnoreQueryFilters().AsNoTracking()
            .Where(dispute => dispute.CustomerId == CustomerA).ToListAsync(CancellationToken);
        IEntityType disputeType = db.Model.FindEntityType(typeof(Dispute)) ?? throw new InvalidOperationException("Dispute is not mapped.");

        Assert.Equal(
            [
                "Escalated/", "Resolved/Rejected", "Resolved/Upheld", "Submitted/", "UnderReview/", "UnderReview/", "Withdrawn/",
            ],
            disputes.Select(dispute => $"{dispute.Status}/{dispute.Outcome}").Order(StringComparer.Ordinal));
        foreach (Dispute row in disputes)
        {
            List<StoredEvent> stream = await StreamAsync(db, row.Id);
            var replayed = Dispute.Replay(stream);

            Assert.Equal(stream.Count, row.Version);
            Assert.All(disputeType.GetProperties(), column =>
                Assert.Equal(column.PropertyInfo?.GetValue(row), column.PropertyInfo?.GetValue(replayed)));
            HttpResponseMessage detail = await a.GetDisputeAsync(row.Id.ToString());
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            using JsonDocument body = await detail.ReadJsonAsync();
            var answered = JsonNode.Parse(body.RootElement.GetProperty("timeline").GetRawText());
            JsonNode? rebuilt = JsonSerializer.SerializeToNode(CustomerView.ToTimeline(stream), DisputeDeskJson.Options);
            Assert.True(JsonNode.DeepEquals(answered, rebuilt), $"The timeline of the {row.Status} dispute is not its replayed stream's.");
        }

        // FR-29: the open ones are recent, so the history reads like live work.
        Assert.All(
            disputes.Where(dispute => dispute.Status is DisputeStatus.Submitted or DisputeStatus.UnderReview),
            dispute => Assert.True(dispute.CreatedAtUtc > clock.NowAsStored.AddDays(-10), $"A {dispute.Status} dispute is 10 days old or more."));
    }

    [Fact]
    public async Task Seed_ClockNotMicrosecondAligned_SlaDemoDisputeIsUnderReviewAndDueFiveMinutesAfterTheSeedingInstant()
    {
        var clock = new TestClock();
        (ApiFactory factory, string connectionString) = await SeededApi.StartAsync(postgres, clock);
        await using ApiFactory _ = factory;
        await using AppDbContext db = SeededApi.Open(connectionString);

        Dispute demo = await db.Set<Dispute>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(dispute => dispute.TransactionId == SlaDemoTransaction, CancellationToken);

        Assert.Equal(DisputeStatus.UnderReview, demo.Status);
        Assert.Equal(clock.NowAsStored.AddMinutes(5), demo.SlaDueAtUtc);
        Assert.Equal(demo.SlaDueAtUtc.AddHours(-240), demo.CreatedAtUtc);
        Assert.True(demo.TransactionDateUtc <= clock.NowAsStored.AddDays(-11), "The SLA demo transaction is less than 11 days old.");
    }

    [Theory]
    [InlineData("00:00:01")] // one second after the anchor, the start of the South African day
    [InlineData("23:59:59")] // the latest seeding instant for the same anchor
    public async Task Seed_AnyTimeOfDay_EveryOpenDisputeButTheSlaDemoIsDueMoreThan7DaysAfterTheSeedingInstant(string southAfricanTime)
    {
        // Today in South Africa at that time, a tick finer than Postgres keeps (UTC+02:00, no daylight saving time).
        DateTimeOffset today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(2));
        DateTimeOffset seededAt = new DateTimeOffset(today.Date.Add(TimeSpan.Parse(southAfricanTime, CultureInfo.InvariantCulture)), TimeSpan.FromHours(2))
            .AddTicks(1_234_567).ToUniversalTime();
        (ApiFactory factory, string connectionString) = await SeededApi.StartAsync(postgres, new FixedTimeProvider(seededAt));
        await using ApiFactory _ = factory;
        await using AppDbContext db = SeededApi.Open(connectionString);

        List<Dispute> open = await db.Set<Dispute>().IgnoreQueryFilters().AsNoTracking()
            .Where(dispute => dispute.Status == DisputeStatus.Submitted || dispute.Status == DisputeStatus.UnderReview)
            .Where(dispute => dispute.TransactionId != SlaDemoTransaction)
            .ToListAsync(CancellationToken);

        Assert.Contains(open, dispute => dispute.CustomerId == CustomerB);
        // Criterion 6's bound; it holds the disputes well clear of story 5.3's 48-hour SLA warning lead time.
        Assert.All(open, dispute => Assert.True(
            dispute.SlaDueAtUtc > seededAt.AddDays(7), $"An open {dispute.Status} dispute is due within 7 days of the seeding instant."));
    }

    [Fact]
    public async Task Seed_EachSeededAction_OccursAtTheInstantResolvedFromItsFixtureEntry()
    {
        var clock = new TestClock();
        (ApiFactory factory, string connectionString) = await SeededApi.StartAsync(postgres, clock);
        await using ApiFactory _ = factory;
        await using AppDbContext db = SeededApi.Open(connectionString);

        foreach (JsonElement fixture in SeededApi.FixtureDisputes())
        {
            string transactionId = fixture.GetProperty("transactionId").GetString() ?? throw new InvalidOperationException("No transactionId.");
            Guid disputeId = await db.Set<Dispute>().IgnoreQueryFilters().AsNoTracking()
                .Where(dispute => dispute.TransactionId == transactionId).Select(dispute => dispute.Id).SingleAsync(CancellationToken);
            List<DateTimeOffset> stored = await db.Set<StoredEventRow>().IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.DisputeId == disputeId).OrderBy(row => row.Version).Select(row => row.OccurredAtUtc)
                .ToListAsync(CancellationToken);

            Assert.Equal(ResolvedInstants(fixture, clock.NowAsStored), stored);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Seed_EitherPath_ReadsTheClockOnce(bool synchronous)
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync();
        var clock = new CountingClock();
        var seeder = new Seeder(
            Options.Create(new SeedOptions { Enabled = true, FixturesPath = FixturesFile.FindPath() }), clock, NullLogger<Seeder>.Instance);
        await using AppDbContext db = SeededApi.Open(connectionString);

        if (synchronous)
        {
            seeder.Seed(db);
        }
        else
        {
            await seeder.SeedAsync(db, CancellationToken);
        }

        Assert.Equal(SeededApi.FullSeed, await SeededApi.CountAsync(connectionString));
        Assert.Equal(1, clock.Reads);
    }

    [Fact]
    public async Task Seed_BsOneDispute_CustomerAGetsTheSameNotFoundAsForAnUnknownId()
    {
        (ApiFactory factory, string connectionString) = await SeededApi.StartAsync(postgres, new TestClock());
        await using ApiFactory _ = factory;
        await using AppDbContext db = SeededApi.Open(connectionString);
        Dispute bs = await db.Set<Dispute>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(dispute => dispute.CustomerId == CustomerB, CancellationToken);
        using HttpClient a = factory.CreateBrowser(new CookieContainer());
        await a.SignInAsync(factory.Zitadel, LoginA);
        string unknown = Guid.CreateVersion7().ToString();

        HttpResponseMessage forTheirs = await a.GetDisputeAsync(bs.Id.ToString());
        HttpResponseMessage forUnknown = await a.GetDisputeAsync(unknown);

        await NotFoundResponsesTests.AssertSameNotFoundAsync(forTheirs, forUnknown, bs.Id.ToString(), unknown);
    }

    [Fact]
    public async Task Seed_EachSeededAction_HasOneStoredEventAndOneOutboxRowUnderTheSameIdByItsActor()
    {
        (ApiFactory factory, string connectionString) = await SeededApi.StartAsync(postgres, new TestClock());
        await using ApiFactory _ = factory;
        await using AppDbContext db = SeededApi.Open(connectionString);

        List<StoredEventRow> storedEvents = await db.Set<StoredEventRow>().IgnoreQueryFilters().AsNoTracking().ToListAsync(CancellationToken);
        List<OutboxMessage> outbox = await db.Set<OutboxMessage>().AsNoTracking().ToListAsync(CancellationToken);
        Dictionary<Guid, string> customerOf = await db.Set<Dispute>().IgnoreQueryFilters().AsNoTracking()
            .ToDictionaryAsync(dispute => dispute.Id, dispute => dispute.CustomerId, CancellationToken);

        Assert.Equal(SeededApi.FullSeed, await SeededApi.CountAsync(connectionString));
        Assert.Equal(storedEvents.Select(row => row.Id).Order(), outbox.Select(message => message.Id).Order());
        var loginOf = new Dictionary<string, string> { [CustomerA] = LoginA, [CustomerB] = LoginB };
        Assert.All(storedEvents, row =>
        {
            // AD-14a: the customer raises and withdraws by their fixture login; the seeder acts for operations as automation.
            (ActorType, string) expected = row.EventType is nameof(DisputeRaised) or nameof(DisputeWithdrawn)
                ? (ActorType.Customer, loginOf[customerOf[row.DisputeId]])
                : (ActorType.Automation, "worker:seeder");
            Assert.Equal(expected, (row.ActorType, row.ActorId));
        });
    }

    [Fact]
    public async Task Seed_DatabaseAlreadyMigrated_SeedsEveryDisputeInsideTheMigrationTransaction()
    {
        // With no migration pending, EF Core seeds inside its own transaction: the customer contexts must join it.
        string connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using ApiFactory factory = SeededApi.Create(connectionString, new TestClock());
        using HttpClient client = factory.CreateClient();

        await HealthTests.WaitUntilReadyAsync(client);

        Assert.Equal(SeededApi.FullSeed, await SeededApi.CountAsync(connectionString));
    }

    [Fact]
    public async Task Seed_ThroughTheApisRegisteredContext_RecordsNoAuditEntryAndNoActionMetric()
    {
        // The seed runs here, after the recorder listens: the Api's own start-up migrate seeds another database.
        await using ApiFactory factory = SeededApi.Create(await postgres.CreateEmptyDatabaseAsync(), new TestClock());
        using var recorder = new MeasurementRecorder(factory.Services.GetRequiredService<IMeterFactory>());
        string connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        DbContextOptions<AppDbContext> registered = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(registered).UseNpgsql(connectionString).Options))
        {
            await db.Database.MigrateAsync(CancellationToken);
        }

        Assert.Equal(SeededApi.FullSeed, await SeededApi.CountAsync(connectionString));
        Assert.Empty(recorder.Measurements("disputedesk.dispute_actions"));
        Assert.Empty(recorder.Measurements("disputedesk.disputes.resolved"));
    }

    [Fact]
    public async Task Seed_EachSnapshot_EqualsTheTransactionTheCoreBankingStubReturnsForTheSameFixtureRow()
    {
        var clock = new TestClock();
        (ApiFactory factory, string connectionString) = await SeededApi.StartAsync(postgres, clock);
        await using ApiFactory _ = factory;
        var bank = Bank.Load(FixturesFile.FindPath(), clock);
        await using AppDbContext db = SeededApi.Open(connectionString);

        List<StoredEventRow> raises = await db.Set<StoredEventRow>().IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.EventType == nameof(DisputeRaised)).ToListAsync(CancellationToken);

        Assert.Equal(SeededApi.FixtureDisputeCount, raises.Count);
        Assert.All(raises, row =>
        {
            var raised = (DisputeRaised)row.ToStoredEvent();
            TransactionSnapshot seeded = raised.Transaction;
            TransactionRow stub = Assert.Single(bank.TransactionsOf(raised.CustomerId) ?? [], transaction => transaction.TransactionId == seeded.TransactionId);
            Assert.Equal(
                (stub.AccountRef, stub.AccountLast4, stub.TransactionDate, stub.Description, stub.Amount, stub.Currency, stub.MaskedCard),
                (seeded.AccountRef, seeded.AccountLast4, seeded.TransactionDate, seeded.Description, seeded.Amount, seeded.Currency, seeded.MaskedCard));
            Assert.Equal(
                (stub.Direction, stub.Kind, stub.State),
                (JsonNamingPolicy.CamelCase.ConvertName(seeded.Direction.ToString()), JsonNamingPolicy.CamelCase.ConvertName(seeded.Kind.ToString()), JsonNamingPolicy.CamelCase.ConvertName(seeded.State.ToString())));
        });
    }

    [Fact]
    public async Task Seed_LogLines_HoldCountsOnly()
    {
        var logs = new LogCapture();
        string connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using (ApiFactory factory = SeededApi.Create(connectionString, new TestClock(), configureServices: services => services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(logs)))
        {
            using HttpClient client = factory.CreateClient();
            await HealthTests.WaitUntilReadyAsync(client);
        }

        await using AppDbContext db = SeededApi.Open(connectionString);
        List<Dispute> disputes = await db.Set<Dispute>().IgnoreQueryFilters().AsNoTracking().ToListAsync(CancellationToken);
        string[] secrets =
        [
            "CUS-", "ACC-", "TXN-",
            .. SeedFixtureTests.PrivateValues(JsonNode.Parse(FixturesFile.ReadText()) ?? throw new InvalidOperationException("No fixtures.")),
            .. disputes.Select(dispute => dispute.Id.ToString()),
        ];

        Assert.Contains($"Seeded {SeededApi.FixtureDisputeCount} disputes", logs.Lines);
        Assert.All(logs.Lines, line => Assert.All(secrets, secret => Assert.DoesNotContain(secret, line, StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<List<StoredEvent>> StreamAsync(AppDbContext db, Guid disputeId)
    {
        List<StoredEventRow> rows = await db.Set<StoredEventRow>().IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.DisputeId == disputeId).OrderBy(row => row.Version).ToListAsync(CancellationToken);
        return [.. rows.Select(row => row.ToStoredEvent())];
    }

    /// <summary>
    /// The instant of each action of a fixture entry, worked out from the rules in <c>fixtures/README.md</c>: days ago
    /// from the anchor at a South African time, hours after the raise, or for the SLA demo raise the instant that makes
    /// it due 5 minutes after <paramref name="seedingInstant"/>.
    /// </summary>
    private static List<DateTimeOffset> ResolvedInstants(JsonElement fixture, DateTimeOffset seedingInstant)
    {
        var anchor = new DateTimeOffset(seedingInstant.ToOffset(SouthAfricanOffset).Date, SouthAfricanOffset);
        bool slaDemo = fixture.TryGetProperty("slaDemo", out JsonElement demo) && demo.GetBoolean();
        var instants = new List<DateTimeOffset>();
        foreach (JsonElement action in fixture.GetProperty("actions").EnumerateArray())
        {
            if (instants.Count == 0 && slaDemo)
            {
                instants.Add(seedingInstant.AddMinutes(5).AddHours(-240));
            }
            else if (action.TryGetProperty("hoursAfterRaise", out JsonElement hours))
            {
                instants.Add(instants[0].AddHours(hours.GetInt32()));
            }
            else
            {
                string time = action.GetProperty("time").GetString() ?? throw new InvalidOperationException("No time.");
                instants.Add(anchor.AddDays(-action.GetProperty("daysAgo").GetInt32()).Add(TimeSpan.Parse(time, CultureInfo.InvariantCulture)));
            }
        }

        return instants;
    }

    /// <summary>A test clock that counts how often it is read.</summary>
    private sealed class CountingClock : TimeProvider
    {
        private readonly TestClock _clock = new();

        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return _clock.GetUtcNow();
        }
    }
}
