using DisputeDesk.Infrastructure.Outbox;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry.Logs;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>Criterion 5: the Kafka options are validated at start, and an unreachable Postgres or broker only delays the relay.</summary>
[Collection(OutboxRelayTestGroup.Name)]
public class OutboxRelayDependencyTests(OutboxFixture outbox)
{
    private const string WarningStart = "Outbox relay cannot reach";
    private const string KafkaFailLine = "Kafka client log line (FAIL)";

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WorkerHost_BlankBootstrapServers_StopsAtStartWithAnOptionsValidationError(string bootstrapServers)
    {
        using IHost host = WorkerHost.CreateBuilder(["--environment", "Development", $"--Kafka:BootstrapServers={bootstrapServers}"]).Build();

        OptionsValidationException error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal(typeof(KafkaOptions), error.OptionsType);
        Assert.Contains("BootstrapServers", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 10)]
    [InlineData(6, 10)]
    [InlineData(1_000, 10)]
    public void Backoff_ConsecutiveFailures_DoublesFromOneSecondAndIsCappedAtTen(int failures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), OutboxRelay.Backoff(failures));
    }

    [Theory]
    [InlineData("", "ConnectionStringMissing")]
    [InlineData("Host=127.0.0.1;Port=1;Username=relay-marker-user;Password=relay-marker-password", "NpgsqlException")]
    public async Task Relay_PostgresUnreachable_WarnsWithoutTheConnectionStringAndRetriesAtLeastEveryTenSeconds(string connectionString, string errorType)
    {
        FakeTimeProvider clock = OutboxFixture.NewClock();

        await using (RelayHost host = await RelayHost.StartAsync(clock, connectionString, outbox.BootstrapServers))
        {
            await OutboxFixture.WaitUntilAsync(() => Warnings(host) >= 1);
            // Each failure waits at most 10 s of the relay's clock before the next try.
            for (int retry = 0; retry < 6; retry++)
            {
                int before = Warnings(host);
                clock.Advance(TimeSpan.FromSeconds(10));
                await OutboxFixture.WaitUntilAsync(() => Warnings(host) > before);
            }

            Assert.All(host.LogLines.Where(line => line.StartsWith(WarningStart, StringComparison.Ordinal)), line =>
            {
                Assert.StartsWith("Outbox relay cannot reach postgres", line, StringComparison.Ordinal);
                Assert.DoesNotContain("relay-marker", line, StringComparison.Ordinal);
                Assert.DoesNotContain("Port=1", line, StringComparison.Ordinal);
            });
            Assert.All(host.Logs.Snapshot().SelectMany(record => record.Attributes ?? []), attribute =>
                Assert.DoesNotContain("relay-marker", attribute.Value?.ToString() ?? "", StringComparison.Ordinal));
            // A missing connection string is a warning like any unreachable database, not an exception the relay throws.
            Assert.All(ErrorTypes(host, "postgres"), type => Assert.Equal(errorType, type));
        }
    }

    [Fact]
    public async Task Relay_BrokerUnreachable_LeavesTheRowPendingWithAttemptsUnchangedAndBacksOff()
    {
        FakeTimeProvider clock = OutboxFixture.NewClock();
        OutboxMessage row = await outbox.RaiseAsync(clock.GetUtcNow());

        // Nothing listens on port 1: the send waits out its 10-second delivery timeout (real time, librdkafka's clock).
        await using (RelayHost host = await RelayHost.StartAsync(clock, outbox.ConnectionString, "127.0.0.1:1"))
        {
            await OutboxFixture.WaitUntilAsync(() => host.LogLines.Any(line => line.StartsWith("Outbox relay cannot reach kafka", StringComparison.Ordinal)));

            Assert.DoesNotContain(host.LogLines, line => line.Contains(row.Payload, StringComparison.Ordinal));
        }

        OutboxMessage after = await outbox.RowAsync(row.Id);
        Assert.Equal((OutboxStatus.Pending, 0, (string?)null, (DateTimeOffset?)null), (after.Status, after.Attempts, after.LastError, after.SentAtUtc));
        Assert.Equal(row.NextAttemptAtUtc, after.NextAttemptAtUtc);
        Assert.Empty(outbox.RecordsFor(row.DisputeId));
    }

    [Fact]
    public async Task Relay_BrokerUnreachable_LogsTheKafkaClientsOwnReportsThroughTheLoggerAndNothingOnStderr()
    {
        FakeTimeProvider clock = OutboxFixture.NewClock();
        TextWriter stderr = Console.Error;
        using var captured = new StringWriter();
        Console.SetError(TextWriter.Synchronized(captured));
        try
        {
            // The producer connects as it is built, so its log and error events start without any row to send.
            await using RelayHost host = await RelayHost.StartAsync(clock, outbox.ConnectionString, "127.0.0.1:1");
            await OutboxFixture.WaitUntilAsync(() =>
                ErrorTypes(host, "kafka").Contains("Local_Transport") && host.LogLines.Contains(KafkaFailLine));

            // The client's error events are the relay's warnings; its own log lines keep their syslog level, mapped by
            // Confluent.Kafka (LogMessage.LevelAs), and carry the facility only.
            Assert.All(host.Logs.Snapshot().Where(record => record.FormattedMessage?.StartsWith(WarningStart, StringComparison.Ordinal) == true), record =>
                Assert.Equal(LogLevel.Warning, record.LogLevel));
            LogRecord fail = host.Logs.Snapshot().First(record => record.FormattedMessage == KafkaFailLine);
            Assert.Equal(LogLevel.Error, fail.LogLevel);
        }
        finally
        {
            Console.SetError(stderr);
        }

        Assert.Equal("", captured.ToString());
    }

    /// <summary>The <c>ErrorType</c> of each warning this host logged about <paramref name="dependency"/>.</summary>
    private static IEnumerable<string> ErrorTypes(RelayHost host, string dependency) =>
        host.Logs.Snapshot()
            .Where(record => record.FormattedMessage?.StartsWith($"{WarningStart} {dependency}", StringComparison.Ordinal) == true)
            .Select(record => record.Attributes?.FirstOrDefault(attribute => attribute.Key == "ErrorType").Value?.ToString() ?? "");

    private static int Warnings(RelayHost host) => host.LogLines.Count(line => line.StartsWith(WarningStart, StringComparison.Ordinal));
}
