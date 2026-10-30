using System.Diagnostics;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Outbox;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Worker.IntegrationTests.Outbox;
using DisputeDesk.Worker.Sla;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace DisputeDesk.Worker.IntegrationTests.Sla;

/// <summary>
/// AC8 (R29, R36; NFR-SEC-5, AD-20; OWASP Logging Cheat Sheet, data to exclude) on the failure path: the Worker host runs
/// the sweep over a customer's disputes, one of whose saves the database refuses and one of whose stream loads it refuses.
/// Every log record of every category the Worker emits (EF Core's and Npgsql's error logs included) and every exported
/// span (Npgsql's child spans included) is captured with the OpenTelemetry in-memory exporters, as <see cref="RelayHost"/>
/// does, and none holds the customer's data.
/// </summary>
[Collection(SlaTestGroup.Name)]
public class SlaTelemetryPrivacyTests(SlaFixture fixture)
{
    [Fact]
    public async Task WorkerHost_SweepWithARefusedSaveAndARefusedLoad_NoLogRecordOrSpanOfAnyCategoryHoldsCustomerData()
    {
        var failLoad = new FailStreamLoad();
        await using SweepRig rig = await SweepRig.CreateAsync(fixture);
        List<Guid> disputeIds = await rig.RaiseManyAsync(3, SweepRig.RaisedAt);
        // The first is escalated, the second's save is refused and the third's stream load is refused.
        Guid refusedSave = disputeIds[1];
        failLoad.DisputeId = disputeIds[2];
        Guid warned = await rig.RaiseAsync(SweepRig.RaisedAt + TimeSpan.FromHours(1));
        string[] transactionIds = [.. await Task.WhenAll(disputeIds.Append(warned).Select(async id => (await rig.DisputeRowAsync(id)).TransactionId))];
        // A refused row is the case where PostgreSQL's error detail would quote the row, customer and snapshot included.
        await RefuseEscalationOfAsync(rig, refusedSave);
        rig.Clock.SetUtcNow(SweepRig.RaisedAt + Dispute.Sla + TimeSpan.FromMinutes(30));

        var spans = new LockedCollection<Activity>();
        var logs = new LockedCollection<LogRecord>();
        var metrics = new LockedCollection<Metric>();
        HostApplicationBuilder builder = WorkerHost.CreateBuilder(
        [
            "--environment", "Development",
            $"--ConnectionStrings:DisputeDesk={rig.ConnectionString}",
            "--Kafka:BootstrapServers=127.0.0.1:1",
        ]);
        builder.Services.AddSingleton<TimeProvider>(rig.Clock);
        // Only the sweep runs: the relay is not under test here and would wait on a broker that is not there.
        builder.Services.Remove(builder.Services.Single(service => service.ImplementationType == typeof(OutboxRelay)));
        builder.Services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failLoad));
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(spans));
        builder.Services.ConfigureOpenTelemetryLoggerProvider(logging => logging.AddInMemoryExporter(logs));
        builder.Services.ConfigureOpenTelemetryMeterProvider(metering => metering.AddInMemoryExporter(metrics));
        using (IHost host = builder.Build())
        {
            await host.StartAsync(TestContext.Current.CancellationToken);
            // The first loop runs at once; it has ended when its warning batch span has.
            await OutboxFixture.WaitUntilAsync(() => spans.Snapshot().Any(span => span.OperationName == SlaSweep.BatchSpanName
                && span.GetTagItem("disputedesk.sla_sweep.claim") as string == "warning"));
            host.Services.GetRequiredService<MeterProvider>().ForceFlush();
            await host.StopAsync(TestContext.Current.CancellationToken);
        }

        // The run took each path: one escalation, one warning, and both refusals, seen by EF Core's logs and Npgsql's spans.
        Dictionary<Guid, (int, int)> counts = await rig.EscalationCountsAsync(disputeIds);
        Assert.Equal([(1, 1), (0, 0), (0, 0)], disputeIds.Select(id => counts[id]));
        Assert.Single(await rig.StreamAsync(warned), row => row.EventType == nameof(SlaWarningRaised));
        Assert.Contains(logs.Snapshot(), record => record.CategoryName?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true
            && record.LogLevel == LogLevel.Error && record.Exception is not null);
        Assert.True(spans.Snapshot().Count(span => span.Source.Name == "Npgsql" && span.Status == ActivityStatusCode.Error) >= 2);

        string[] forbidden = [SweepRig.CustomerId, SweepRig.AccountRef, .. transactionIds, "TXN-SLA-", "Shop SLA description", "87.23", "login-sla"];
        List<string> observed = TelemetryValues(logs, spans, metrics);
        Assert.NotEmpty(observed);
        Assert.All(forbidden, value => Assert.DoesNotContain(observed, text => text.Contains(value, StringComparison.Ordinal)));
        // The sweep's own failure lines carry the dispute ID and the exception's type, never the exception.
        Assert.All(logs.Snapshot().Where(record => record.CategoryName == typeof(SlaSweep).FullName), record => Assert.Null(record.Exception));
    }

    /// <summary>A check constraint refuses the escalation of one dispute, as a failing save would be refused.</summary>
    private static async Task RefuseEscalationOfAsync(SweepRig rig, Guid disputeId)
    {
        await using var connection = new NpgsqlConnection(rig.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            $"""ALTER TABLE "Disputes" ADD CONSTRAINT refuse_escalation CHECK ("Id" <> '{disputeId}' OR "Status" <> 'Escalated')""", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Every log record's message, body, exception and attribute values; every span's name, tags, status and events; every metric tag.</summary>
    private static List<string> TelemetryValues(LockedCollection<LogRecord> logs, LockedCollection<Activity> spans, LockedCollection<Metric> metrics)
    {
        var values = new List<string>();
        values.AddRange(logs.Snapshot().SelectMany(record =>
            new[] { record.FormattedMessage, record.Body, record.Exception?.ToString() }
                .Concat((record.Attributes ?? []).Select(attribute => attribute.Value?.ToString()))
                .OfType<string>()));
        values.AddRange(spans.Snapshot().SelectMany(span =>
            new[] { span.DisplayName, span.StatusDescription }
                .Concat(span.TagObjects.Select(tag => tag.Value?.ToString()))
                .Concat(span.Events.SelectMany(spanEvent => spanEvent.Tags.Select(tag => tag.Value?.ToString()).Prepend(spanEvent.Name)))
                .OfType<string>()));
        foreach (Metric metric in metrics.Snapshot())
        {
            foreach (ref readonly MetricPoint point in metric.GetMetricPoints())
            {
                foreach (KeyValuePair<string, object?> tag in point.Tags)
                {
                    values.Add($"{tag.Key}={tag.Value}");
                }
            }
        }

        return values;
    }
}
