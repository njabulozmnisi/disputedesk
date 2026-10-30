using System.Diagnostics;
using System.Diagnostics.Metrics;
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
/// An unreachable dependency only delays it; a record the client permanently refuses is retried and set Failed on the
/// 5th refusal; on shutdown it finishes the one record in flight and takes no new one (AD-25).
/// <para>
/// <c>buildProducer</c> builds the producer from the relay's configured builder. The Worker registers none, so DI passes
/// the default, null, and the relay calls <c>Build()</c> (Microsoft Learn, .NET dependency injection: a constructor
/// argument DI does not provide takes its default value); a test registers one to make sends fail with a chosen
/// <see cref="Error"/>.
/// </para>
/// </summary>
public sealed partial class OutboxRelay(
    IOptions<KafkaOptions> kafkaOptions,
    IConfiguration configuration,
    TimeProvider timeProvider,
    IMeterFactory meterFactory,
    ILogger<OutboxRelay> logger,
    Func<ProducerBuilder<string, string>, IProducer<string, string>>? buildProducer = null) : BackgroundService
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
    private const int MaxAttempts = 5;
    private static readonly TimeSpan IdleWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(10);
    private static readonly ActivitySource Source = new(Telemetry.ActivitySourceName);

    /// <summary>
    /// Rows set Failed (AD-20), with no tags, on the DisputeDesk Meter created through <see cref="IMeterFactory"/>
    /// (Microsoft Learn, Creating metrics).
    /// </summary>
    private readonly Counter<long> _failed = meterFactory.Create(Telemetry.MeterName)
        .CreateCounter<long>("disputedesk.outbox.failed", description: "Outbox rows set Failed after 5 permanent refusals.");

    /// <summary>What a failed send means for its row (AD-17, Failures).</summary>
    internal enum SendFailure
    {
        /// <summary>The broker unavailable, a timeout, leadership moving, a missing topic or any unlisted code: no attempt counts.</summary>
        Transient,

        /// <summary>The record itself is refused, and would be on every try: counts an attempt.</summary>
        Rejected,

        /// <summary>The producer can no longer be used and is recreated: no attempt counts.</summary>
        Fatal,
    }

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

    /// <summary>
    /// Classifies a delivery error, once. A fatal error is fatal whatever its code: the idempotent producer can no longer
    /// be used (librdkafka INTRODUCTION.md, fatal errors; Confluent.Kafka reports it with <see cref="Error.IsFatal"/>). Only
    /// a permanent refusal of the record counts an attempt; every other error is transient, because librdkafka already
    /// retries a retriable error until <c>message.timeout.ms</c>.
    /// </summary>
    internal static SendFailure Classify(Error error)
    {
        if (error.IsFatal)
        {
            return SendFailure.Fatal;
        }

        return error.Code is ErrorCode.MsgSizeTooLarge or ErrorCode.InvalidMsg or ErrorCode.InvalidMsgSize or ErrorCode.InvalidRecord or ErrorCode.Local_BadMsg
            ? SendFailure.Rejected
            : SendFailure.Transient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Each producer span's parent is the row's stored context or none, never whatever started the host.
        Activity.Current = null;
        IProducer<string, string> producer = BuildProducer();
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
                    int handled = lockConnection is null ? 0 : await PublishBatchAsync(lockConnection, producer, stoppingToken);
                    failures = 0;
                    wait = handled > 0 ? TimeSpan.Zero : IdleWait;
                }
                catch (ProduceException<string, string> exception) when (Classify(exception.Error) == SendFailure.Fatal)
                {
                    // The row stays Pending; the producer is disposed and built again (librdkafka INTRODUCTION.md, fatal errors).
                    LogProducerRecreated(exception.Error.Code.ToString());
                    producer.Dispose();
                    producer = BuildProducer();
                    wait = Backoff(++failures);
                }
                catch (ProduceException<string, string> exception)
                {
                    // Transient: the row stays Pending with no attempt counted, and this instance keeps the lock.
                    LogSendDeferred(exception.Error.Code.ToString());
                    wait = Backoff(++failures);
                }
                catch (Exception exception) when (IsPostgresFailure(exception))
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

            producer.Dispose();
        }
    }

    /// <summary>
    /// A failure of the database or its connection. EF Core reports a failed update as a <see cref="DbUpdateException"/>,
    /// and Npgsql's default execution strategy wraps a transient failure, also one inside a <see cref="DbUpdateException"/>,
    /// in an <see cref="InvalidOperationException"/> ("likely due to a transient failure"; Npgsql EF Core,
    /// NpgsqlExecutionStrategy). Any other <see cref="InvalidOperationException"/> is a bug and stops the relay.
    /// </summary>
    private static bool IsPostgresFailure(Exception exception) =>
        exception is NpgsqlException or DbUpdateException
        || exception is InvalidOperationException { InnerException: NpgsqlException or DbUpdateException };

    /// <summary>
    /// librdkafka's own log lines keep their level, mapped by Confluent.Kafka (<see cref="LogMessage.LevelAs"/>), and its
    /// error events become the relay's warning, instead of going to stderr. Each carries only the facility or the error
    /// code, never the text, which can hold a broker address or a record value.
    /// </summary>
    private IProducer<string, string> BuildProducer()
    {
        ProducerBuilder<string, string> builder = new ProducerBuilder<string, string>(CreateProducerConfig(kafkaOptions.Value))
            .SetLogHandler((_, log) =>
            {
                var level = (LogLevel)log.LevelAs(LogLevelType.MicrosoftExtensionsLogging);
                LogKafkaClient(level, log.Facility);
            })
            .SetErrorHandler((_, error) => LogDependencyUnavailable("kafka", error.Code.ToString()));
        return buildProducer is null ? builder.Build() : buildProducer(builder);
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
    /// in turn and records the outcome with the batch's one instant (AD-4). Returns how many rows it handled.
    /// </summary>
    private async Task<int> PublishBatchAsync(NpgsqlConnection lockConnection, IProducer<string, string> producer, CancellationToken stoppingToken)
    {
        await using (var check = new NpgsqlCommand("SELECT 1", lockConnection))
        {
            await check.ExecuteScalarAsync(stoppingToken);
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
            .ToListAsync(stoppingToken);

        foreach (OutboxMessage message in heads)
        {
            // Shutdown takes no new record (AD-25).
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            // Touched on both sides of the send, so the file is never older than one send's 10-second delivery timeout.
            TouchHeartbeat();
            // The record in flight is never cancelled: its delivery report and update finish, bounded by the 10-second
            // delivery timeout and the 5-second command timeout (AD-25; BackgroundService). ProduceAsync's token would
            // only stop the wait, not the delivery.
            ErrorCode? refusal = await SendAsync(producer, message);
            if (refusal is null)
            {
                message.Status = OutboxStatus.Sent;
                message.SentAtUtc = now;
            }
            else
            {
                CountRefusal(message, refusal.Value, now);
            }

            await db.SaveChangesAsync(CancellationToken.None);
            if (message.Status == OutboxStatus.Failed)
            {
                _failed.Add(1);
                LogFailed(message.Id, message.DisputeId);
            }

            TouchHeartbeat();
        }

        return heads.Count;
    }

    /// <summary>
    /// Counts a permanent refusal (AD-17, Failures). <c>LastError</c> holds the code alone, never <c>Error.Reason</c>, which is
    /// broker free text. Attempts 1 to 4 wait 1, 2, 4 then 8 seconds from the batch's instant, each times a random 0.8 to
    /// 1.2 (Azure Architecture Center, exponential back-off with jitter); the 5th sets Failed and keeps the 4th's next try.
    /// </summary>
    private static void CountRefusal(OutboxMessage message, ErrorCode code, DateTimeOffset now)
    {
        message.Attempts++;
        message.LastError = code.ToString();
        if (message.Attempts >= MaxAttempts)
        {
            message.Status = OutboxStatus.Failed;
            return;
        }

        message.NextAttemptAtUtc = now + (Backoff(message.Attempts) * (0.8 + (0.4 * Random.Shared.NextDouble())));
    }

    /// <summary>
    /// Produces one row and waits for its delivery report. Returns null once the broker confirmed it, or the code of a
    /// permanent refusal; any other failure is thrown. The stored trace context is read and the headers written by the
    /// OpenTelemetry propagator only (W3C Trace Context), with no baggage; the span follows the OpenTelemetry messaging and
    /// Kafka semantic conventions; the value is the committed CloudEvent, byte for byte (CloudEvents Kafka protocol
    /// binding, structured mode).
    /// </summary>
    private static async Task<ErrorCode?> SendAsync(IProducer<string, string> producer, OutboxMessage message)
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

        try
        {
            await producer.ProduceAsync(Topic, new Message<string, string> { Key = key, Value = message.Payload, Headers = headers }, CancellationToken.None);
            return null;
        }
        catch (ProduceException<string, string> exception)
        {
            // error.type is the Kafka error code (OpenTelemetry semantic conventions, recording errors); the status has no
            // description and no exception event is recorded, so no broker text or record value reaches the trace.
            span?.SetTag("error.type", exception.Error.Code.ToString());
            span?.SetStatus(ActivityStatusCode.Error);
            if (Classify(exception.Error) != SendFailure.Rejected)
            {
                throw;
            }

            return exception.Error.Code;
        }
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

    // The failure-path lines carry no connection string, exception, message text or record value: only which dependency,
    // the error's type or code, or the row's event and dispute IDs.
    // The relay retries with Backoff; librdkafka reconnects on its own schedule, so the line names no delay.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox relay cannot reach {Dependency} ({ErrorType})")]
    private partial void LogDependencyUnavailable(string dependency, string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox relay could not send an event ({ErrorCode}); it stays Pending and is retried")]
    private partial void LogSendDeferred(string errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox relay's Kafka producer hit a fatal error ({ErrorCode}) and is recreated")]
    private partial void LogProducerRecreated(string errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox event {EventId} of dispute {DisputeId} is Failed after 5 permanent refusals; the dispute's later events wait for an operator")]
    private partial void LogFailed(Guid eventId, Guid disputeId);

    [LoggerMessage(Message = "Kafka client log line ({Facility})")]
    private partial void LogKafkaClient(LogLevel level, string facility);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox relay holds the outbox lock and publishes")]
    private partial void LogHoldsLock();
}
