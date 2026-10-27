using System.Diagnostics;
using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using OpenTelemetry.Logs;

namespace DisputeDesk.Api.IntegrationTests.Transactions;

/// <summary>AC8: a traced transactions request exports no seeded customer ID, and its log lines carry its trace ID.</summary>
public class TransactionsTelemetryTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private static readonly string[] SeededCustomerIds = ["CUS-000001", "CUS-000002", "CUS-000003", "CUS-000004"];

    [Fact]
    public async Task ListTransactions_Traced_ExportsNoCustomerIdAndEveryLogLineCarriesTheTraceId()
    {
        var telemetry = new TelemetryCapture();
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub, telemetry.AddTo);
        await factory.LinkAsync("login-a", TransactionsApi.CustomerA);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "login-a");
        // A trace ID of the test's own, so spans from tests running in parallel are never mistaken for this request's.
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/transactions", UriKind.Relative));
        request.Headers.Add("traceparent", $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
        int logsBefore = telemetry.Logs.Count;

        HttpResponseMessage response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The server span ends last, so once it is exported the database and core-banking spans are too.
        await telemetry.WaitForSpanAsync(span => span.TraceId == traceId && span.Kind == ActivityKind.Server);
        IReadOnlyList<Activity> spans = telemetry.Spans(traceId);
        Assert.Contains(spans, span => span.Source.Name == "System.Net.Http");
        Assert.Contains(spans, span => span.Source.Name == "Npgsql");
        Assert.All(spans, span => AssertNoCustomerId(SpanTexts(span)));

        IReadOnlyList<LogRecord> requestLogs = [.. telemetry.Logs.Skip(logsBefore)];
        Assert.NotEmpty(requestLogs);
        Assert.All(requestLogs, log => Assert.Equal(traceId, log.TraceId));
        // Every line this Api logged in the test, sign-in included.
        Assert.All(telemetry.Logs, log => AssertNoCustomerId(LogTexts(log)));
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

    private static void AssertNoCustomerId(IEnumerable<string?> texts)
    {
        foreach (string text in texts.OfType<string>())
        {
            Assert.All(SeededCustomerIds, customerId => Assert.DoesNotContain(customerId, text, StringComparison.Ordinal));
        }
    }
}
