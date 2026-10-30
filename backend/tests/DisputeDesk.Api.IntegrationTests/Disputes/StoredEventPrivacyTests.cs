using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Operations;
using DisputeDesk.Api.IntegrationTests.Transactions;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Logs;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AD-22, NFR-SEC-5: a stored event holds the customer ID, the account reference, the transaction snapshot, the note and
/// the actor's login, and none of them reaches a log line, a span attribute or a metric label while a raise commits.
/// <c>EnableSensitiveDataLogging</c> stays off outside tests (<see cref="DatabaseLoggingTests"/>).
/// </summary>
public class StoredEventPrivacyTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private const string LoginSub = "login-a";
    private const string Note = "privacy-note-marker";

    // Customer A's disputable transaction as the fixtures hold it: its ID, masked card and description.
    private static readonly string[] Private =
        [TransactionsApi.CustomerA, DisputesApi.AccountA, DisputesApi.Disputable, "492181******4417", "Steers Braamfontein", Note, LoginSub];

    [Fact]
    public async Task Raise_Commits_NoLogSpanOrMetricLabelHoldsAnyStoredEventData()
    {
        var telemetry = new TelemetryCapture();
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, telemetry.AddTo);
        using var labels = new LabelRecorder(factory.Services.GetRequiredService<IMeterFactory>());
        using HttpClient browser = await factory.SignInLinkedAsync(LoginSub, TransactionsApi.CustomerA);
        // A trace ID of the test's own, so spans from tests running in parallel are never mistaken for this request's.
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(DisputesApi.RaisePath, UriKind.Relative))
        {
            Content = new StringContent(DisputesApi.Body(note: Note), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", await browser.AntiforgeryTokenAsync());
        request.Headers.Add("Idempotency-Key", DisputesApi.NewKey());
        request.Headers.Add("traceparent", $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");

        HttpResponseMessage response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // The server span ends last, so once it is exported the database spans of the commit are too.
        await telemetry.WaitForSpanAsync(span => span.TraceId == traceId && span.Kind == ActivityKind.Server);
        IReadOnlyList<Activity> spans = telemetry.Spans(traceId);
        Assert.Contains(spans, span => span.Source.Name == "Npgsql");
        Assert.All(spans, span => AssertNothingPrivate(SpanTexts(span)));
        Assert.NotEmpty(telemetry.Logs);
        Assert.All(telemetry.Logs, log => AssertNothingPrivate(LogTexts(log)));
        Assert.NotEmpty(labels.Values);
        AssertNothingPrivate(labels.Values);
    }

    [Fact]
    public async Task Withdraw_SucceedsThen409And404_NoLogSpanOrMetricLabelHoldsTheNoteTheLoginOrTheCustomer()
    {
        var telemetry = new TelemetryCapture();
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, telemetry.AddTo);
        using var labels = new LabelRecorder(factory.Services.GetRequiredService<IMeterFactory>());
        using HttpClient browser = await factory.SignInLinkedAsync(LoginSub, TransactionsApi.CustomerA);
        string disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetString() ?? "";
        string json = DisputesApi.WithdrawBody(Note);

        // Each with a trace ID of the test's own, as above: the success, the repeat (409) and an unknown ID (404).
        (HttpStatusCode Status, ActivityTraceId TraceId)[] withdraws =
        [
            await WithdrawTracedAsync(browser, disputeId, json),
            await WithdrawTracedAsync(browser, disputeId, json),
            await WithdrawTracedAsync(browser, Guid.CreateVersion7().ToString(), json),
        ];

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.NotFound], withdraws.Select(withdraw => withdraw.Status));
        foreach ((HttpStatusCode _, ActivityTraceId traceId) in withdraws)
        {
            await telemetry.WaitForSpanAsync(span => span.TraceId == traceId && span.Kind == ActivityKind.Server);
            IReadOnlyList<Activity> spans = telemetry.Spans(traceId);
            Assert.Contains(spans, span => span.Source.Name == "Npgsql");
            Assert.All(spans, span => AssertNothingPrivate(SpanTexts(span)));
        }

        Assert.All(telemetry.Logs, log => AssertNothingPrivate(LogTexts(log)));
        Assert.Contains("withdraw", labels.Values);
        AssertNothingPrivate(labels.Values);
    }

    [Fact]
    public async Task Resolve_SucceedsThen409404And400_TheNoteIsInNoLogSpanLabelOutboxPayloadAuditRowOrProblemBody()
    {
        // Story 5.2, AC8 (NFR-SEC-6; ASVS 5.0 V16.2.5): the bank's note is for the customer's timeline only.
        const string ResolveNote = "resolve-note-marker";
        var telemetry = new TelemetryCapture();
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, telemetry.AddTo);
        using var labels = new LabelRecorder(factory.Services.GetRequiredService<IMeterFactory>());
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();
        Assert.Equal(HttpStatusCode.OK, (await factory.ActAsMappedAsync(tool, disputeId, "start-review")).StatusCode);
        string token = factory.Zitadel.Issue(roles: OperationsApi.ResolveRole);
        string json = OperationsApi.ResolveBody("upheld", ResolveNote);
        string tooLong = OperationsApi.ResolveBody("upheld", ResolveNote + new string('a', 500));

        // The success, the repeat (409), an unknown ID (404) and a note too long (400), each traced on its own.
        (HttpResponseMessage Response, ActivityTraceId TraceId)[] resolves =
        [
            await ResolveTracedAsync(tool, disputeId.ToString(), token, json),
            await ResolveTracedAsync(tool, disputeId.ToString(), token, json),
            await ResolveTracedAsync(tool, Guid.CreateVersion7().ToString(), token, json),
            await ResolveTracedAsync(tool, disputeId.ToString(), token, tooLong),
        ];

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.NotFound, HttpStatusCode.BadRequest],
            resolves.Select(resolve => resolve.Response.StatusCode));
        foreach ((HttpResponseMessage response, ActivityTraceId _) in resolves.Skip(1))
        {
            Assert.DoesNotContain(ResolveNote, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }

        foreach ((HttpResponseMessage _, ActivityTraceId traceId) in resolves)
        {
            await telemetry.WaitForSpanAsync(span => span.TraceId == traceId && span.Kind == ActivityKind.Server);
            Assert.All(telemetry.Spans(traceId), span => AssertNotIn(ResolveNote, SpanTexts(span)));
        }

        Assert.All(telemetry.Logs, log => AssertNotIn(ResolveNote, LogTexts(log)));
        Assert.Contains("resolve", labels.Values);
        Assert.All(new[] { ResolveNote, disputeId.ToString(), FakeZitadel.OperationsSub }, value => AssertNotIn(value, labels.Values));
        Assert.All(await factory.OutboxRowsAsync(disputeId), message => Assert.DoesNotContain(ResolveNote, message.Payload, StringComparison.Ordinal));
        Assert.All(await factory.AuditRowsAsync(), entry => AssertNotIn(ResolveNote, [entry.ActorId, entry.CustomerId, entry.TraceId, entry.Action.ToString(), entry.Result.ToString()]));
    }

    private static async Task<(HttpResponseMessage Response, ActivityTraceId TraceId)> ResolveTracedAsync(HttpClient tool, string disputeId, string token, string json)
    {
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{OperationsApi.DisputesPath}/{disputeId}/resolve", UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("traceparent", $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
        return (await tool.SendAsync(request, TestContext.Current.CancellationToken), traceId);
    }

    private static void AssertNotIn(string value, IEnumerable<string?> texts)
    {
        foreach (string text in texts.OfType<string>())
        {
            Assert.DoesNotContain(value, text, StringComparison.Ordinal);
        }
    }

    private static async Task<(HttpStatusCode Status, ActivityTraceId TraceId)> WithdrawTracedAsync(HttpClient browser, string disputeId, string json)
    {
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(DisputesApi.WithdrawPath(disputeId), UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", await browser.AntiforgeryTokenAsync());
        request.Headers.Add("traceparent", $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
        HttpResponseMessage response = await browser.SendAsync(request, TestContext.Current.CancellationToken);
        return (response.StatusCode, traceId);
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

    private static void AssertNothingPrivate(IEnumerable<string?> texts)
    {
        foreach (string text in texts.OfType<string>())
        {
            Assert.All(Private, value => Assert.DoesNotContain(value, text, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Every label value of every instrument of one Api's meters. A <see cref="MeterListener"/> listens only to meters
    /// of that Api's <see cref="IMeterFactory"/>, so measurements of tests running alongside are never recorded here.
    /// </summary>
    private sealed class LabelRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<string?> _values = new();

        public LabelRecorder(IMeterFactory meterFactory)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Scope == meterFactory)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) => Record(tags));
            _listener.SetMeasurementEventCallback<double>((_, _, tags, _) => Record(tags));
            _listener.Start();
        }

        public IReadOnlyList<string?> Values => [.. _values];

        public void Dispose() => _listener.Dispose();

        private void Record(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                _values.Enqueue(tag.Value?.ToString());
            }
        }
    }
}
