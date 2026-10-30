using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.Time.Testing;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// Criterion 4: the trace continues from the stored context through a producer span (OpenTelemetry messaging and Kafka
/// semantic conventions) into W3C headers written by the propagator (AD-20).
/// </summary>
[Collection(OutboxRelayTestGroup.Name)]
public class OutboxRelayTraceTests(OutboxFixture outbox)
{
    private const string StoredTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string StoredSpanId = "00f067aa0ba902b7";
    private const string ValidTraceParent = $"00-{StoredTraceId}-{StoredSpanId}-01";

    // 33 list members: W3C allows at most 32, and OpenTelemetry .NET 1.20.1's TraceContextPropagator drops the whole
    // tracestate on Extract when a list has more (TryExtractSingleTracestate, "memberCount >= Limit").
    private static readonly string TooManyMembers = string.Join(',', Enumerable.Range(0, 33).Select(index => $"k{index}=v"));

    public static TheoryData<string, string?, string?, bool, string?> StoredContexts => new()
    {
        { "valid, no tracestate", ValidTraceParent, null, true, null },
        { "valid with tracestate", ValidTraceParent, "disputedesk=test", true, "disputedesk=test" },
        { "none", null, null, false, null },
        { "malformed traceparent", "00-not-a-trace-parent", "disputedesk=test", false, null },
        { "tracestate breaking the 32-member rule", ValidTraceParent, TooManyMembers, true, null },
    };

    [Theory]
    [MemberData(nameof(StoredContexts))]
    public async Task Relay_StoredTraceContext_ContinuesTheTraceThroughAProducerSpanAndThePropagatorsHeaders(
        string scenario, string? traceParent, string? traceState, bool hasParent, string? expectedTraceState)
    {
        FakeTimeProvider clock = OutboxFixture.NewClock();
        OutboxMessage row = await outbox.RaiseAsync(clock.GetUtcNow());
        await outbox.SetTraceContextAsync(row.Id, traceParent, traceState);

        Activity span;
        await using (RelayHost host = await outbox.StartRelayAsync(clock))
        {
            await outbox.WaitUntilSentAsync([row.Id]);
            await OutboxFixture.WaitUntilAsync(() => host.ProducerSpansFor(row.Id).Count == 1);
            span = Assert.Single(host.ProducerSpansFor(row.Id));
        }

        Assert.Equal("send disputedesk.disputes", span.DisplayName);
        Assert.Equal(ActivityKind.Producer, span.Kind);
        Assert.Equal(
            new Dictionary<string, object?>
            {
                ["messaging.system"] = "kafka",
                ["messaging.operation.name"] = "send",
                ["messaging.operation.type"] = "send",
                ["messaging.destination.name"] = "disputedesk.disputes",
                ["messaging.kafka.message.key"] = row.DisputeId.ToString(),
                ["messaging.message.id"] = row.Id.ToString(),
            },
            span.TagObjects.ToDictionary());
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
        if (hasParent)
        {
            Assert.Equal(StoredTraceId, span.TraceId.ToHexString());
            Assert.Equal(StoredSpanId, span.ParentSpanId.ToHexString());
        }
        else
        {
            Assert.Equal(default, span.ParentSpanId);
        }

        Headers headers = Assert.Single(outbox.RecordsFor(row.DisputeId)).Message.Headers;
        string[] expectedNames = expectedTraceState is null ? ["content-type", "traceparent"] : ["content-type", "traceparent", "tracestate"];
        Assert.True(expectedNames.SequenceEqual(headers.Select(header => header.Key).Order()), scenario);
        Assert.Equal($"00-{span.TraceId.ToHexString()}-{span.SpanId.ToHexString()}-01", Header(headers, "traceparent"));
        if (expectedTraceState is not null)
        {
            Assert.Equal(expectedTraceState, Header(headers, "tracestate"));
        }
    }

    private static string Header(Headers headers, string name) => Encoding.UTF8.GetString(headers.GetLastBytes(name));
}
