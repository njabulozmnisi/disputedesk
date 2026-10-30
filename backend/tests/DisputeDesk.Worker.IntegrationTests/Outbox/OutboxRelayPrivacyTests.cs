using System.Globalization;
using System.Text;
using Confluent.Kafka;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry.Metrics;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// Criterion 7: no personal data crosses the Kafka boundary or the relay's telemetry (NFR-SEC-5; AD-20; OWASP Logging
/// Cheat Sheet, data to exclude). Every private field of <see cref="DisputeRaised"/> and the actor ID carry a marker.
/// </summary>
[Collection(OutboxRelayTestGroup.Name)]
public class OutboxRelayPrivacyTests(OutboxFixture outbox)
{
    private const string CustomerId = "customer-pmark-1";
    private const string AccountRef = "ACC-PMARK-2";
    private const string TransactionId = "TXN-PMARK-3";
    private const string Description = "Shop PMARK description";
    private const string MaskedCard = "492181******PMRK";
    // Not hex digits, so no trace ID, span ID or GUID can hold it by chance.
    private const string AccountLast4 = "QZXW";
    private const string Note = "note PMARK text";
    private const string ActorId = "login-pmark-4";
    private const decimal Amount = 9876.53m;
    private const decimal ExpectedAmount = 1234.56m;

    [Fact]
    public async Task Relay_PublishesARaise_NoLogSpanMetricTagOrHeaderHoldsAPrivateField()
    {
        FakeTimeProvider clock = OutboxFixture.NewClock();
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset transactionDate = now.AddDays(-2);
        string[] markers =
        [
            CustomerId, AccountRef, TransactionId, Description, MaskedCard, AccountLast4, Note, ActorId, "PMARK",
            transactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            // The amounts; the disputed amount is the transaction amount less the expected one.
            "9876.53", "1234.56", "8641.97",
        ];
        var transaction = new TransactionSnapshot(
            AccountRef, TransactionId, AccountLast4, transactionDate, Description, Amount, "ZAR", MaskedCard,
            TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);
        Dispute dispute = Dispute.Raise(
            CustomerId, transaction, everDisputed: false, Reason.WrongAmount, Note, ExpectedAmount,
            new Actor(ActorType.Customer, ActorId), now).Value;
        OutboxMessage row = await outbox.CommitAsync(dispute);

        var observed = new List<string>();
        await using (RelayHost host = await outbox.StartRelayAsync(clock))
        {
            await outbox.WaitUntilSentAsync([row.Id]);
            await OutboxFixture.WaitUntilAsync(() => host.ProducerSpansFor(row.Id).Count == 1);
            host.Services.GetRequiredService<MeterProvider>().ForceFlush();

            observed.AddRange(host.Logs.Snapshot().SelectMany(record =>
                new[] { record.FormattedMessage, record.Body }.Concat((record.Attributes ?? []).Select(attribute => attribute.Value?.ToString()))
                    .OfType<string>()));
            observed.AddRange(host.Spans.Snapshot().SelectMany(span =>
                new[] { span.DisplayName }.Concat(span.TagObjects.Select(tag => tag.Value?.ToString())).OfType<string>()));
            observed.AddRange(host.Metrics.Snapshot().SelectMany(MetricTagValues));
        }

        ConsumeResult<string, string> record = Assert.Single(outbox.RecordsFor(row.DisputeId));
        observed.Add(record.Message.Key);
        observed.AddRange(record.Message.Headers.Select(header => header.Key + "=" + Encoding.UTF8.GetString(header.GetValueBytes())));

        Assert.NotEmpty(observed);
        Assert.All(markers, marker => Assert.DoesNotContain(observed, value => value.Contains(marker, StringComparison.Ordinal)));
        // The record value is the committed payload, whose content 3.8's tests guard (PublishMappingTests, RaisedEventTests).
        Assert.Equal(row.Payload, record.Message.Value);
    }

    private static IEnumerable<string> MetricTagValues(Metric metric)
    {
        var values = new List<string>();
        foreach (ref readonly MetricPoint point in metric.GetMetricPoints())
        {
            foreach (KeyValuePair<string, object?> tag in point.Tags)
            {
                values.Add($"{tag.Key}={tag.Value}");
            }
        }

        return values;
    }
}
