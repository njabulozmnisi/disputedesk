using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;
using DisputeDesk.Infrastructure.Outbox;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redpanda;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// One real PostgreSQL and one Redpanda for the relay tests. The database is migrated once and shared, so every test
/// inserts its own rows for fresh dispute IDs and asserts only on those rows and on Kafka records with those keys; none
/// assumes an empty outbox or topic (AD-17, Seeded rows).
/// </summary>
public sealed class OutboxFixture : IAsyncLifetime
{
    // The Compose image, pinned by tag and digest (AD-27), not the module's default tag.
    private const string RedpandaImage =
        "docker.redpanda.com/redpandadata/redpanda:v26.2.2@sha256:468bd13a9f2bd24794cb7fddc867c767fb1008b9a07b297b89fde48c564d7d96";

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
    private readonly RedpandaContainer _redpanda = new RedpandaBuilder(RedpandaImage).Build();

    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>
    /// For a test connection that holds the advisory lock. Unpooled, as the relay's own, so disposing it ends the session
    /// and releases the lock; a pooled one would go back to the pool still holding it (PostgreSQL, Advisory Locks).
    /// </summary>
    public string LockHolderConnectionString => new NpgsqlConnectionStringBuilder(ConnectionString) { Pooling = false }.ConnectionString;

    public string BootstrapServers => new Uri(_redpanda.GetBootstrapAddress()).Authority;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redpanda.StartAsync());
        await using (AppDbContext db = CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }

        // As the Compose redpanda-topics service does: the dispute events topic with 3 partitions.
        using IAdminClient admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        await admin.CreateTopicsAsync([new TopicSpecification { Name = OutboxRelay.Topic, NumPartitions = 3, ReplicationFactor = 1 }]);
    }

    public async ValueTask DisposeAsync()
    {
        await _redpanda.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>A test clock at the current whole second, so instants survive Postgres's microsecond precision and file times exactly.</summary>
    public static FakeTimeProvider NewClock()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        return new FakeTimeProvider(now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond)));
    }

    public Task<RelayHost> StartRelayAsync(TimeProvider clock) => RelayHost.StartAsync(clock, ConnectionString, BootstrapServers);

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>Raises a dispute through the commit (AD-14), which writes its version 1 outbox row due at <paramref name="at"/>.</summary>
    public async Task<OutboxMessage> RaiseAsync(DateTimeOffset at)
    {
        var transaction = new TransactionSnapshot(
            "ACC-T-1", $"TXN-{Guid.NewGuid():N}", "0011", at.AddDays(-2), "Test shop", 87.23m, "ZAR", null,
            TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);
        Dispute dispute = Dispute.Raise(
            "customer-t", transaction, everDisputed: false, Reason.Unrecognised, note: null, expectedAmount: null,
            new Actor(ActorType.Customer, "login-t"), at).Value;
        return await CommitAsync(dispute);
    }

    /// <summary>Commits a newly raised dispute and returns its outbox row.</summary>
    public async Task<OutboxMessage> CommitAsync(Dispute dispute)
    {
        await using AppDbContext db = CreateDbContext();
        db.Set<Dispute>().Add(dispute);
        await db.SaveChangesAsync();
        return await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(message => message.DisputeId == dispute.Id);
    }

    /// <summary>A hand-built later version of a dispute's outbox row; only <c>Raise</c> exists to write one through the commit.</summary>
    public async Task<OutboxMessage> AddVersionAsync(Guid disputeId, int version, DateTimeOffset at)
    {
        var id = Guid.CreateVersion7(at);
        var message = new OutboxMessage
        {
            Id = id,
            EventType = "disputedesk.dispute.status-changed.v1",
            DisputeId = disputeId,
            DisputeVersion = version,
            Payload = JsonSerializer.Serialize(new { id, data = new { disputeVersion = version } }),
            Status = OutboxStatus.Pending,
            NextAttemptAtUtc = at,
            CreatedAtUtc = at,
        };
        await using AppDbContext db = CreateDbContext();
        db.Set<OutboxMessage>().Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    /// <summary>Changes one outbox row directly, as only a test does.</summary>
    public async Task UpdateRowAsync(Guid id, Action<OutboxMessage> change)
    {
        await using AppDbContext db = CreateDbContext();
        OutboxMessage message = await db.Set<OutboxMessage>().SingleAsync(row => row.Id == id);
        change(message);
        await db.SaveChangesAsync();
    }

    /// <summary>Sets the stored trace context columns, which the commit takes from <c>Activity.Current</c>.</summary>
    public async Task SetTraceContextAsync(Guid id, string? traceParent, string? traceState)
    {
        await using AppDbContext db = CreateDbContext();
        await db.Set<OutboxMessage>()
            .Where(row => row.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.TraceParent, traceParent)
                .SetProperty(row => row.TraceState, traceState));
    }

    public async Task<OutboxMessage> RowAsync(Guid id)
    {
        await using AppDbContext db = CreateDbContext();
        return await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(row => row.Id == id);
    }

    /// <summary>Waits until every given row is Sent, moving <paramref name="clock"/> on by 1 second per step when given.</summary>
    public async Task WaitUntilSentAsync(IEnumerable<Guid> ids, FakeTimeProvider? clock = null)
    {
        Guid[] wanted = [.. ids];
        await WaitUntilAsync(
            async () =>
            {
                await using AppDbContext db = CreateDbContext();
                int sent = await db.Set<OutboxMessage>().CountAsync(row => wanted.Contains(row.Id) && row.Status == OutboxStatus.Sent);
                return sent == wanted.Length;
            },
            clock);
    }

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, FakeTimeProvider? clock = null)
    {
        var elapsed = Stopwatch.StartNew();
        while (!await condition())
        {
            if (elapsed.Elapsed > WaitLimit)
            {
                throw new TimeoutException($"The condition was not met within {WaitLimit}.");
            }

            clock?.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(50);
        }
    }

    public static Task WaitUntilAsync(Func<bool> condition, FakeTimeProvider? clock = null) =>
        WaitUntilAsync(() => Task.FromResult(condition()), clock);

    /// <summary>Every record now in the topic whose key is one of <paramref name="disputeIds"/>, in partition offset order.</summary>
    public List<ConsumeResult<string, string>> RecordsFor(params Guid[] disputeIds)
    {
        HashSet<string> keys = [.. disputeIds.Select(id => id.ToString())];
        var config = new ConsumerConfig { BootstrapServers = BootstrapServers, GroupId = $"tests-{Guid.NewGuid():N}", EnableAutoCommit = false };
        using IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(config).Build();
        var records = new List<ConsumeResult<string, string>>();
        for (int partition = 0; partition < 3; partition++)
        {
            var topicPartition = new TopicPartition(OutboxRelay.Topic, partition);
            WatermarkOffsets offsets = consumer.QueryWatermarkOffsets(topicPartition, TimeSpan.FromSeconds(10));
            if (offsets.High <= offsets.Low)
            {
                continue;
            }

            consumer.Assign(new TopicPartitionOffset(topicPartition, offsets.Low));
            ConsumeResult<string, string> record;
            do
            {
                record = consumer.Consume(TimeSpan.FromSeconds(10)) ?? throw new TimeoutException($"Partition {partition} ended early.");
                if (keys.Contains(record.Message.Key))
                {
                    records.Add(record);
                }
            }
            while (record.Offset < offsets.High - 1);
        }

        return records;
    }
}
