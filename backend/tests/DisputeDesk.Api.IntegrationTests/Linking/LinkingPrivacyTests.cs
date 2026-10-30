using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
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
/// 6.1 AC8 and 6.2 AC8, NFR-SEC-5: the ID number, the full account number, the cellphone, the plaintext OTP and the HMAC
/// of a submitted wrong code reach no table, no log line and no span while customer D links, a failed send and a wrong
/// code included, and the correct code's HMAC is only in <c>LinkingChallenges.OtpHmac</c>; the records that hold them
/// print no field.
/// </summary>
public class LinkingPrivacyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Linking_FailedSendSentAndVerified_NoTableLogOrSpanHoldsTheDetailsTheCellphoneTheOtpOrACodesHmac()
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
        ActivityTraceId[] verifyTraces = [ActivityTraceId.CreateRandom(), ActivityTraceId.CreateRandom()];

        ports.Sms.Failure = new InvalidOperationException($"5.1.1 <{LinkingFakes.D.Cellphone}@sms.disputedesk.test>: Recipient address rejected");
        await StartTracedAsync(browser, requestTraces[0]);
        await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Failed);
        ports.Sms.Failure = null;
        await StartTracedAsync(browser, requestTraces[1]);
        await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded);

        string[] otps = [.. ports.Sms.Received.Select(received => Regex.Match(received.Message, "[0-9]{6}").Value)];
        Assert.Equal(2, otps.Length);
        // The released OTP, submitted to the current challenge, counts as a wrong code; the sent one links.
        Guid currentChallenge = (await factory.CurrentChallengeAsync(login))?.Id ?? throw new InvalidOperationException("No current challenge.");
        byte[] key = Convert.FromBase64String(ApiFactory.TestOtpHmacKey);
        byte[] wrongHmac = Otp.Hmac(key, currentChallenge, otps[0]);
        byte[] correctHmac = Otp.Hmac(key, currentChallenge, otps[1]);
        await VerifyTracedAsync(browser, otps[0], verifyTraces[0]);
        await VerifyTracedAsync(browser, otps[1], verifyTraces[1]);
        Assert.Single(await factory.LinksAsync());
        string[] personal = [LinkingFakes.D.IdNumber, LinkingFakes.D.AccountNumber, LinkingFakes.D.Cellphone];

        List<(string Table, string Json)> rows = await EveryRowAsJsonAsync(database);
        AssertNoneIn(rows.Select(row => row.Json), personal, otps);
        Assert.DoesNotContain(rows, row => HoldsHmac(row.Json, wrongHmac));
        // The correct code's HMAC is the stored OtpHmac (bytea as \x hex), in that one column of that one row.
        (string table, string json) = Assert.Single(rows, row => HoldsHmac(row.Json, correctHmac));
        Assert.Equal("LinkingChallenges", table);
        JsonObject challengeRow = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("Not a row.");
        Assert.Equal(
            ["OtpHmac"],
            challengeRow.Where(column => column.Value is not null && HoldsHmac(column.Value.ToJsonString(), correctHmac)).Select(column => column.Key));
        Assert.Equal($"\\x{Convert.ToHexStringLower(correctHmac)}", challengeRow["OtpHmac"]?.GetValue<string>());
        Assert.NotEmpty(telemetry.Logs);
        AssertNoneIn(telemetry.Logs.SelectMany(LogTexts), personal, otps);
        Assert.DoesNotContain(telemetry.Logs.SelectMany(LogTexts).OfType<string>(), text => HoldsHmac(text, wrongHmac) || HoldsHmac(text, correctHmac));
        // The request spans, the job spans linked to them, every span in the jobs' traces, and the verify requests' spans.
        List<Activity> jobs = [];
        foreach (ActivityTraceId requestTrace in requestTraces)
        {
            jobs.Add(await telemetry.WaitForSpanAsync(span => span.Links.Any(link => link.Context.TraceId == requestTrace)));
        }

        foreach (ActivityTraceId verifyTrace in verifyTraces)
        {
            await telemetry.WaitForSpanAsync(span => span.TraceId == verifyTrace && span.Kind == ActivityKind.Server);
        }

        List<Activity> spans = [.. requestTraces.Concat(verifyTraces).Concat(jobs.Select(job => job.TraceId)).SelectMany(telemetry.Spans)];
        Assert.Contains(spans, span => span.Source.Name == "Npgsql" && verifyTraces.Contains(span.TraceId));
        AssertNoneIn(spans.SelectMany(SpanTexts), personal, otps);
        Assert.DoesNotContain(spans.SelectMany(SpanTexts).OfType<string>(), text => HoldsHmac(text, wrongHmac) || HoldsHmac(text, correctHmac));
    }

    [Fact]
    public void Records_HoldingPersonalData_PrintNoField()
    {
        var job = new LinkingJob(Guid.NewGuid(), "login-x", IPAddress.Loopback, default, LinkingFakes.D.IdNumber, LinkingFakes.D.AccountNumber);
        var match = new CustomerMatch(LinkingFakes.D.CustomerId, LinkingFakes.D.Cellphone);
        var request = new StartLinkingRequest(LinkingFakes.D.IdNumber, LinkingFakes.D.AccountNumber);
        var verify = new VerifyOtpRequest("123456");

        Assert.Equal("LinkingJob", job.ToString());
        Assert.Equal("CustomerMatch", match.ToString());
        Assert.Equal("StartLinkingRequest", request.ToString());
        Assert.Equal("VerifyOtpRequest", verify.ToString());
    }

    private static async Task VerifyTracedAsync(HttpClient browser, string code, ActivityTraceId traceId) =>
        await browser.VerifyAsync(
            VerifyApi.Body(code),
            configure: request => request.Headers.Add("traceparent", $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01"));

    private static async Task StartTracedAsync(HttpClient browser, ActivityTraceId traceId)
    {
        HttpResponseMessage response = await browser.StartLinkingAsync(
            LinkingApi.Body(LinkingFakes.D),
            configure: request => request.Headers.Add("traceparent", $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    /// <summary>Every row of every table in the test's database, with its table, as JSON text (bytea as <c>\x</c> hex).</summary>
    private static async Task<List<(string Table, string Json)>> EveryRowAsJsonAsync(string database)
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
        List<(string Table, string Json)> rows = [];
        foreach (string table in tables)
        {
            // The names come from the catalogue, not from input; quoted because they are PascalCase.
            await using var command = new NpgsqlCommand($"SELECT row_to_json(t)::text FROM \"{table}\" t", connection);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                rows.Add((table, reader.GetString(0)));
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

    /// <summary>The HMAC as hex in either case, or as Base64 (which is case-sensitive).</summary>
    private static bool HoldsHmac(string text, byte[] hmac) =>
        text.Contains(Convert.ToHexString(hmac), StringComparison.OrdinalIgnoreCase)
        || text.Contains(Convert.ToBase64String(hmac), StringComparison.Ordinal);

    /// <summary>
    /// None of the values as text, nor as the hex of their UTF-8 bytes in either case, which is how JSON shows a bytea
    /// column. An OTP counts only as a whole token: bytea hex, GUIDs and trace IDs hold many random 6-digit runs. Its hex
    /// form is 12 characters, so a plain search for it is safe.
    /// </summary>
    private static void AssertNoneIn(IEnumerable<string?> texts, string[] values, string[] otps)
    {
        string[] needles = [.. values, .. values.Concat(otps).Select(value => Convert.ToHexStringLower(Encoding.UTF8.GetBytes(value)))];
        Regex[] otpTokens = [.. otps.Select(otp => new Regex($"(?<![0-9A-Za-z]){otp}(?![0-9A-Za-z])"))];
        foreach (string text in texts.OfType<string>())
        {
            Assert.DoesNotContain(needles, needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(otpTokens, otpToken => otpToken.IsMatch(text));
        }
    }
}
