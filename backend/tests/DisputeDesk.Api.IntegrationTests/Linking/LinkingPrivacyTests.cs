using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Linking;
using Npgsql;
using OpenTelemetry.Logs;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.1 AC8, NFR-SEC-5: the ID number, the full account number, the cellphone and the plaintext OTP reach no table, no
/// log line and no span while customer D links, a failed send included; the records that hold them print no field.
/// </summary>
public class LinkingPrivacyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Linking_SentAndFailedSend_NoTableLogOrSpanHoldsTheDetailsTheCellphoneOrTheOtp()
    {
        var ports = new LinkingFakes.Ports();
        var telemetry = new TelemetryCapture();
        string database = await postgres.CreateMigratedDatabaseAsync();
        await using var factory = new ApiFactory(
            database,
            configureServices: services =>
            {
                ports.AddTo(services);
                telemetry.AddTo(services);
            },
            clock: new TestClock());
        string login = LinkingApi.NewLogin("privacy");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        ActivityTraceId[] requestTraces = [ActivityTraceId.CreateRandom(), ActivityTraceId.CreateRandom()];

        await StartTracedAsync(browser, requestTraces[0]);
        await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded);
        ports.Sms.Failure = new InvalidOperationException($"5.1.1 <{LinkingFakes.D.Cellphone}@sms.disputedesk.test>: Recipient address rejected");
        await StartTracedAsync(browser, requestTraces[1]);
        await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Failed);

        string[] otps = [.. ports.Sms.Received.Select(received => Regex.Match(received.Message, "[0-9]{6}").Value)];
        Assert.Equal(2, otps.Length);
        string[] personal = [LinkingFakes.D.IdNumber, LinkingFakes.D.AccountNumber, LinkingFakes.D.Cellphone];

        AssertNoneIn(await EveryRowAsJsonAsync(database), personal, otps);
        Assert.NotEmpty(telemetry.Logs);
        AssertNoneIn(telemetry.Logs.SelectMany(LogTexts), personal, otps);
        // The request spans, the job spans linked to them, and every span in the jobs' traces.
        List<Activity> jobs = [];
        foreach (ActivityTraceId requestTrace in requestTraces)
        {
            jobs.Add(await telemetry.WaitForSpanAsync(span => span.Links.Any(link => link.Context.TraceId == requestTrace)));
        }

        List<Activity> spans = [.. requestTraces.Concat(jobs.Select(job => job.TraceId)).SelectMany(telemetry.Spans)];
        Assert.Contains(spans, span => span.Source.Name == "Npgsql");
        AssertNoneIn(spans.SelectMany(SpanTexts), personal, otps);
    }

    [Fact]
    public void Records_HoldingPersonalData_PrintNoField()
    {
        var job = new LinkingJob(Guid.NewGuid(), "login-x", IPAddress.Loopback, default, LinkingFakes.D.IdNumber, LinkingFakes.D.AccountNumber);
        var match = new CustomerMatch(LinkingFakes.D.CustomerId, LinkingFakes.D.Cellphone);
        var request = new StartLinkingRequest(LinkingFakes.D.IdNumber, LinkingFakes.D.AccountNumber);

        Assert.Equal("LinkingJob", job.ToString());
        Assert.Equal("CustomerMatch", match.ToString());
        Assert.Equal("StartLinkingRequest", request.ToString());
    }

    private static async Task StartTracedAsync(HttpClient browser, ActivityTraceId traceId)
    {
        HttpResponseMessage response = await browser.StartLinkingAsync(
            LinkingApi.Body(LinkingFakes.D),
            configure: request => request.Headers.Add("traceparent", $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    /// <summary>Every row of every table in the test's database, as JSON text (bytea as <c>\x</c> hex).</summary>
    private static async Task<List<string>> EveryRowAsJsonAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        List<string> tables = [];
        await using (var command = new NpgsqlCommand("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'", connection))
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }

        Assert.Contains("LinkingChallenges", tables);
        List<string> rows = [];
        foreach (string table in tables)
        {
            // The names come from the catalogue, not from input; quoted because they are PascalCase.
            await using var command = new NpgsqlCommand($"SELECT row_to_json(t)::text FROM \"{table}\" t", connection);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                rows.Add(reader.GetString(0));
            }
        }

        return rows;
    }

    private static IEnumerable<string?> LogTexts(LogRecord log) =>
    [
        log.FormattedMessage,
        log.Body,
        log.Exception?.ToString(),
        .. (log.Attributes ?? []).Select(attribute => attribute.Value?.ToString()),
    ];

    private static IEnumerable<string?> SpanTexts(Activity span) =>
    [
        span.DisplayName,
        span.StatusDescription,
        .. span.TagObjects.Select(tag => tag.Value?.ToString()),
        .. span.Events.SelectMany(spanEvent => spanEvent.Tags.Select(tag => tag.Value?.ToString())),
    ];

    /// <summary>
    /// None of the values as text, nor as the hex of their UTF-8 bytes, which is how JSON shows a bytea column. An OTP
    /// counts only as a whole token: bytea hex, GUIDs and trace IDs hold many random 6-digit runs. Its hex form is 12
    /// characters, so a plain search for it is safe.
    /// </summary>
    private static void AssertNoneIn(IEnumerable<string?> texts, string[] values, string[] otps)
    {
        string[] needles = [.. values, .. values.Concat(otps).Select(value => Convert.ToHexStringLower(Encoding.UTF8.GetBytes(value)))];
        Regex[] otpTokens = [.. otps.Select(otp => new Regex($"(?<![0-9A-Za-z]){otp}(?![0-9A-Za-z])"))];
        foreach (string text in texts.OfType<string>())
        {
            Assert.DoesNotContain(needles, needle => text.Contains(needle, StringComparison.Ordinal));
            Assert.DoesNotContain(otpTokens, otpToken => otpToken.IsMatch(text));
        }
    }
}
