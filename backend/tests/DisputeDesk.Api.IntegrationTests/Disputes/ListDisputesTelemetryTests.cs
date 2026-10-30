using System.Diagnostics;
using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using OpenTelemetry.Logs;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// Story 3.9 AC3 (AD-20): a list call carrying another customer's ID in its query exports no span attribute and logs
/// no line holding either customer's ID. The ASP.NET Core instrumentation records <c>url.query</c> with its values
/// redacted by default (OpenTelemetry.Instrumentation.AspNetCore 1.20.1), and the test reads it to prove that.
/// </summary>
public class ListDisputesTelemetryTests(PostgresFixture postgres)
{
    private static readonly string[] CustomerIds = [TransactionsApi.CustomerA, TransactionsApi.CustomerB];

    [Fact]
    public async Task ListDisputes_CustomerIdInTheQuery_ExportsAndLogsNeitherCustomersId()
    {
        var telemetry = new TelemetryCapture();
        await using ApiFactory factory = await DisputesApi.StartWithoutCoreBankingAsync(postgres, telemetry.AddTo);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        await factory.SeedDisputeAsync(TransactionsApi.CustomerA, "TXN-A-1", DisputesApi.SeedInstant);
        await factory.SeedDisputeAsync(TransactionsApi.CustomerB, "TXN-B-1", DisputesApi.SeedInstant);
        // A trace ID of the test's own, so spans from tests running in parallel are never mistaken for this request's.
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{DisputesApi.RaisePath}?customerId={TransactionsApi.CustomerB}", UriKind.Relative));
        request.Headers.Add("traceparent", $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");

        HttpResponseMessage response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The server span ends last, so once it is exported the database spans are too.
        Activity server = await telemetry.WaitForSpanAsync(span => span.TraceId == traceId && span.Kind == ActivityKind.Server);
        Assert.Equal("?customerId=Redacted", server.GetTagItem("url.query"));
        IReadOnlyList<Activity> spans = telemetry.Spans(traceId);
        Assert.Contains(spans, span => span.Source.Name == "Npgsql");
        Assert.All(spans, span => AssertNoCustomerId(SpanTexts(span)));
        // Every line this Api logged in the test, sign-in and seeding included.
        Assert.NotEmpty(telemetry.Logs);
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
            Assert.All(CustomerIds, customerId => Assert.DoesNotContain(customerId, text, StringComparison.Ordinal));
        }
    }
}
