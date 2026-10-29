using System.Diagnostics;
using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;
using OpenTelemetry.Logs;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AC6: a stored row of a type this code does not know is left out of the detail, never a 500 (AD-13); a known-type row
/// whose data cannot be read is a clean 500. Neither row's data reaches a log line, a span or the body (AD-22; OWASP
/// Logging Cheat Sheet, data to exclude). Replay keeps refusing the unknown row
/// (<see cref="StoredEventReplayTests.LoadDispute_RowOfAnUnknownEventType_ThrowsRatherThanReturnADisputeOrNotFound"/>).
/// </summary>
public class StoredRowSkipTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private const string UnknownMarker = "unknown-row-marker-5d1e";
    private const string CorruptMarker = "corrupt-row-marker-9b2a";

    [Fact]
    public async Task GetDispute_RowOfAnUnknownEventType_Returns200WithoutItAndNothingHoldsItsData()
    {
        var telemetry = new TelemetryCapture();
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, telemetry.AddTo);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();
        await factory.AddRowsAsync(Row(disputeId, "DisputeReopened", $$"""{"note":"{{UnknownMarker}}"}"""));

        (HttpResponseMessage response, ActivityTraceId traceId) = await GetTracedAsync(browser, disputeId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var dispute = JsonDocument.Parse(body);
        JsonElement entry = Assert.Single(dispute.RootElement.GetProperty("timeline").EnumerateArray());
        Assert.Equal("raise", entry.GetProperty("action").GetString());
        await AssertNowhereAsync(UnknownMarker, body, telemetry, traceId);
    }

    [Fact]
    public async Task GetDispute_KnownTypeRowWhoseDataCannotBeRead_Returns500WithOnlyTraceIdAndNothingHoldsItsData()
    {
        var telemetry = new TelemetryCapture();
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, telemetry.AddTo);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();
        // Valid JSON, as jsonb requires, but a status that names no member.
        await factory.AddRowsAsync(Row(
            disputeId,
            nameof(DisputeResolved),
            $$"""{"fromStatus":"{{CorruptMarker}}","toStatus":"resolved","outcome":"upheld","note":"{{CorruptMarker}}"}"""));

        (HttpResponseMessage response, ActivityTraceId traceId) = await GetTracedAsync(browser, disputeId);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Internal), await response.ProblemTypeAsync());
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(["type", "title", "status", "traceId"], problem.RootElement.EnumerateObject().Select(property => property.Name));
        await AssertNowhereAsync(CorruptMarker, body, telemetry, traceId);
    }

    private StoredEventRow Row(Guid disputeId, string eventType, string data) => new()
    {
        Id = Guid.CreateVersion7(stub.Now),
        DisputeId = disputeId,
        Version = 2,
        EventType = eventType,
        OccurredAtUtc = stub.NowAsStored,
        ActorType = ActorType.Operations,
        ActorId = "ops-sub",
        Data = data,
    };

    /// <summary>GETs the detail under a trace ID of the test's own, so spans of tests running alongside never count.</summary>
    private static async Task<(HttpResponseMessage Response, ActivityTraceId TraceId)> GetTracedAsync(HttpClient browser, Guid disputeId)
    {
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{DisputesApi.RaisePath}/{disputeId}", UriKind.Relative));
        request.Headers.Add("traceparent", $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
        return (await browser.SendAsync(request, TestContext.Current.CancellationToken), traceId);
    }

    private static async Task AssertNowhereAsync(string marker, string body, TelemetryCapture telemetry, ActivityTraceId traceId)
    {
        Assert.DoesNotContain(marker, body, StringComparison.Ordinal);
        // The server span ends last, so once it is exported the request's other spans are too.
        await telemetry.WaitForSpanAsync(span => span.TraceId == traceId && span.Kind == ActivityKind.Server);
        IReadOnlyList<Activity> spans = telemetry.Spans(traceId);
        Assert.Contains(spans, span => span.Source.Name == "Npgsql");
        Assert.All(spans.SelectMany(SpanTexts).OfType<string>(), text => Assert.DoesNotContain(marker, text, StringComparison.Ordinal));
        Assert.NotEmpty(telemetry.Logs);
        Assert.All(telemetry.Logs.SelectMany(LogTexts).OfType<string>(), text => Assert.DoesNotContain(marker, text, StringComparison.Ordinal));
    }

    private static IEnumerable<string?> SpanTexts(Activity span) =>
    [
        span.DisplayName,
        span.StatusDescription,
        .. span.TagObjects.Select(tag => tag.Value?.ToString()),
        .. span.Events.SelectMany(spanEvent => spanEvent.Tags.Select(tag => tag.Value?.ToString())),
    ];

    private static IEnumerable<string?> LogTexts(LogRecord log) =>
    [
        log.FormattedMessage,
        log.Body,
        log.Exception?.ToString(),
        .. (log.Attributes ?? []).Select(attribute => attribute.Value?.ToString()),
    ];
}
