using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using DisputeDesk.Infrastructure.Observability;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace DisputeDesk.Infrastructure.Outbox;

/// <summary>
/// The single outbox relay (AD-17). Whoever holds a PostgreSQL advisory lock publishes the head row of each dispute to
/// Kafka, one send at a time, and marks it Sent once the broker confirms it. Every other instance only retries the lock.
/// Unreachable dependencies only delay it: a failed send leaves its row Pending (story 4.2 adds per-record failures).
/// </summary>
public sealed partial class OutboxRelay(
    IOptions<KafkaOptions> kafkaOptions,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    public const string Topic = "disputedesk.disputes";

    /// <summary>
    /// Touched every loop; the Compose health check fails when it is older than 30 seconds (AD-25). The system temporary
    /// directory is <c>/tmp/</c> in the container (no TMPDIR) and the user's own temporary directory on a developer machine.
    /// </summary>
    public static readonly string HeartbeatPath = Path.Combine(Path.GetTempPath(), "heartbeat-relay");

    /// <summary>The advisory lock every relay instance competes for. Any fixed number works, as long as it never changes.</summary>
    public const long LockKey = 4_017_001;

    private const int BatchSize = 100;
    private static readonly TimeSpan IdleWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(10);
    private static readonly ActivitySource Source = new(Telemetry.ActivitySourceName);

    /// <summary>
    /// The producer settings (AD-17, Producer): idempotent with every in-sync replica acknowledging, and a send given up
    /// after 10 seconds so a loop never outlives the heartbeat threshold.
    /// </summary>
    internal static ProducerConfig CreateProducerConfig(KafkaOptions options) => new()
    {
        // shortcut: plaintext, no client auth on the private Compose network; upgrade to TLS plus SASL/SCRAM or mTLS with topic ACLs (only the relay principal writes) before a shared network
        BootstrapServers = options.BootstrapServers,
        EnableIdempotence = true,
        Acks = Acks.All,
        MessageTimeoutMs = 10_000,
    };

    /// <summary>The wait after <paramref name="failures"/> failures in a row: 1 second, doubling, at most 10 seconds.</summary>
    internal static TimeSpan Backoff(int failures) =>
        TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, failures - 1)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Each producer span's parent is the row's stored context or none, never whatever started the host.
        Activity.Current = null;
        // librdkafka's own log lines and error events (a broker down, a refused connection) become the relay's warning
        // instead of going to stderr; they carry only the facility or error code, never the text or a record value.
        using IProducer<string, string> producer = new ProducerBuilder<string, string>(CreateProducerConfig(kafkaOptions.Value))
            .SetLogHandler((_, log) => LogDependencyUnavailable("kafka", log.Facility))
            .SetErrorHandler((_, error) => LogDependencyUnavailable("kafka", error.Code.ToString()))
            .Build();
        string connectionString = configuration.GetConnectionString(DependencyInjection.ConnectionStringName) ?? "";
        NpgsqlConnection? lockConnection = null;
        int failures = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan wait;
                TouchHeartbeat();
                if (connectionString.Length == 0)
                {
                    // Nothing to connect to: the same warning and backoff as an unreachable database.
                    wait = Backoff(++failures);
                    LogDependencyUnavailable("postgres", "ConnectionStringMissing");
                    await Task.Delay(wait, timeProvider, stoppingToken);
                    continue;
                }

                try
                {
                    lockConnection ??= await TryTakeLockAsync(connectionString, stoppingToken);
                    int sent = lockConnection is null ? 0 : await PublishBatchAsync(lockConnection, producer, stoppingToken);
                    failures = 0;
                    wait = sent > 0 ? TimeSpan.Zero : IdleWait;
                }
                catch (ProduceException<string, string> exception)
                {
                    // The broker is unreachable or slow: the row stays Pending and this instance keeps the lock.
                    wait = Backoff(++failures);
                    LogDependencyUnavailable("kafka", exception.Error.Code.ToString());
                }
                // EF Core reports a dropped connection as an InvalidOperationException ("likely due to a transient failure")
                // around the NpgsqlException, so that wrapper counts too; any other InvalidOperationException is a bug.
                catch (Exception exception) when (exception is NpgsqlException or DbUpdateException || exception.InnerException is NpgsqlException)
                {
                    // The lock may be gone with the connection, so give both up and produce nothing until the lock is retaken.
                    if (lockConnection is not null)
                    {
                        await lockConnection.DisposeAsync();
                        lockConnection = null;
                    }

                    wait = Backoff(++failures);
                    LogDependencyUnavailable("postgres", exception.GetType().Name);
                }

                TouchHeartbeat();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, timeProvider, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
        finally
        {
            if (lockConnection is not null)
            {
                await lockConnection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Opens a dedicated connection and tries the advisory lock on it, returning the connection only when the lock is
    /// held. A session-level advisory lock lasts as long as its connection, so it is unpooled: disposing it releases the
    /// lock (PostgreSQL, Advisory Locks). Short timeouts keep a dead server from stalling the loop past the heartbeat.
    /// </summary>
    private async Task<NpgsqlConnection?> TryTakeLockAsync(string connectionString, CancellationToken cancellationToken)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, Timeout = 5, CommandTimeout = 5 };
        var connection = new NpgsqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.Add(new NpgsqlParameter<long>("key", LockKey));
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                LogHoldsLock();
                return connection;
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    /// <summary>
    /// One batch on the lock connection: checks the connection still answers, then sends each dispute's due head row
    /// in turn and marks it Sent with the batch's one instant (AD-4). Returns how many rows it sent.
    /// </summary>
    private async Task<int> PublishBatchAsync(NpgsqlConnection lockConnection, IProducer<string, string> producer, CancellationToken cancellationToken)
    {
        await using (var check = new NpgsqlCommand("SELECT 1", lockConnection))
        {
            await check.ExecuteScalarAsync(cancellationToken);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        // The context runs on the lock connection and does not own it (EF Core, using an existing connection).
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(lockConnection).Options);

        // The head of a dispute is its lowest version not yet Sent; a Failed or not-yet-due head holds back the rest.
        // shortcut: no index for head selection; add IX_OutboxMessages_DisputeId_DisputeVersion, filtered on "Status" <> 'Sent', when the outbox grows
        List<OutboxMessage> heads = await db.Set<OutboxMessage>()
            .Where(message => message.Status == OutboxStatus.Pending && message.NextAttemptAtUtc <= now)
            .Where(message => !db.Set<OutboxMessage>().Any(earlier =>
                earlier.DisputeId == message.DisputeId
                && earlier.DisputeVersion < message.DisputeVersion
                && earlier.Status != OutboxStatus.Sent))
            .OrderBy(message => message.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (OutboxMessage message in heads)
        {
            // Touched on both sides of the send, so the file is never older than one send's 10-second delivery timeout.
            TouchHeartbeat();
            await SendAsync(producer, message, cancellationToken);
            message.Status = OutboxStatus.Sent;
            message.SentAtUtc = now;
            // Keeps the stopping token for now: story 4.2 owns graceful shutdown, including a stop right after a delivery.
            await db.SaveChangesAsync(cancellationToken);
            TouchHeartbeat();
        }

        return heads.Count;
    }

    /// <summary>
    /// Produces one row and waits for its delivery report. The stored trace context is read and the headers written by
    /// the OpenTelemetry propagator only (W3C Trace Context), with no baggage; the span follows the OpenTelemetry
    /// messaging and Kafka semantic conventions; the value is the committed CloudEvent, byte for byte (CloudEvents Kafka
    /// protocol binding, structured mode).
    /// </summary>
    private static async Task SendAsync(IProducer<string, string> producer, OutboxMessage message, CancellationToken cancellationToken)
    {
        PropagationContext stored = Propagators.DefaultTextMapPropagator.Extract(default, message, StoredTraceContext);
        string key = message.DisputeId.ToString();
        using Activity? span = Source.StartActivity(
            $"send {Topic}",
            ActivityKind.Producer,
            stored.ActivityContext,
            [
                new("messaging.system", "kafka"),
                new("messaging.operation.name", "send"),
                new("messaging.operation.type", "send"),
                new("messaging.destination.name", Topic),
                new("messaging.kafka.message.key", key),
                new("messaging.message.id", message.Id.ToString()),
            ]);

        var headers = new Headers { { "content-type", "application/cloudevents+json"u8.ToArray() } };
        var context = new PropagationContext(span?.Context ?? stored.ActivityContext, default(Baggage));
        Propagators.DefaultTextMapPropagator.Inject(context, headers, (carrier, name, value) => carrier.Add(name, Encoding.UTF8.GetBytes(value)));

        await producer.ProduceAsync(Topic, new Message<string, string> { Key = key, Value = message.Payload, Headers = headers }, cancellationToken);
    }

    /// <summary>The propagator's carrier: the row's two trace context columns under their W3C header names.</summary>
    private static IEnumerable<string>? StoredTraceContext(OutboxMessage message, string name) => name switch
    {
        "traceparent" when message.TraceParent is not null => [message.TraceParent],
        "tracestate" when message.TraceState is not null => [message.TraceState],
        _ => null,
    };

    private void TouchHeartbeat()
    {
        if (!File.Exists(HeartbeatPath))
        {
            File.WriteAllBytes(HeartbeatPath, []);
        }

        File.SetLastWriteTimeUtc(HeartbeatPath, timeProvider.GetUtcNow().UtcDateTime);
    }

    // Carries no connection string, exception message or record value: only which dependency and the error's type or code.
    // The relay retries with Backoff; librdkafka reconnects on its own schedule, so the line names no delay.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox relay cannot reach {Dependency} ({ErrorType})")]
    private partial void LogDependencyUnavailable(string dependency, string errorType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox relay holds the outbox lock and publishes")]
    private partial void LogHoldsLock();
}
