using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.5 AC3: an unexpected exception in the job rolls back its transaction, which releases the customer's lock, claims
/// and sends nothing, is audited as <c>LinkingStart</c> <c>Failed</c>, logs only the challenge ID and the exception
/// type, and the next job still runs.
/// </summary>
public class LinkingUnexpectedStopTests(PostgresFixture postgres)
{
    public static TheoryData<string> Throwers => ["sim swap", "core banking"];

    [Theory]
    [MemberData(nameof(Throwers))]
    public async Task Job_PortThrowsAnUnexpectedException_AuditsLinkingStartFailedClaimsNothingAndTheNextStartSends(string thrower)
    {
        var ports = new LinkingFakes.Ports();
        // As a bug could: the message quotes the details the job holds.
        var unexpected = new InvalidCastException($"Unexpected value {LinkingFakes.D.IdNumber}");
        string? expectedCustomerId = null;
        if (thrower == "sim swap")
        {
            ports.SimSwap.ThrowOnNextCall(unexpected);
            expectedCustomerId = LinkingFakes.D.CustomerId;
        }
        else
        {
            ports.CoreBanking.ThrowOnNextCall(unexpected);
        }

        var telemetry = new TelemetryCapture();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock(), telemetry.AddTo);
        string login = LinkingApi.NewLogin("unexpected");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        await browser.StartAcceptedAsync(LinkingFakes.D);

        List<AuditLogEntry> entries = await factory.WaitForAuditAsync(login, AuditAction.LinkingStart, AuditResult.Failed);
        Assert.Equal(
            [(AuditAction.LinkingStart, AuditResult.Succeeded, null), (AuditAction.LinkingStart, AuditResult.Failed, expectedCustomerId)],
            entries.Select(entry => (entry.Action, entry.Result, entry.CustomerId)));
        Assert.Empty(ports.Sms.Received);
        LinkingChallenge challenge = Assert.Single(await factory.ChallengesAsync(login));
        Assert.Equal((null, null, null), (challenge.CustomerId, challenge.OtpHmac, challenge.OtpSentAtUtc));
        Assert.Equal(0, await factory.AdvisoryLocksAsync(granted: true));
        LogRecord error = Assert.Single(telemetry.Logs, log => log.LogLevel == LogLevel.Error && log.CategoryName == "DisputeDesk.Infrastructure.Linking.LinkingJobSteps");
        Assert.Null(error.Exception);
        Assert.Equal(
            [("ChallengeId", challenge.Id.ToString()), ("ErrorType", nameof(InvalidCastException))],
            (error.Attributes ?? []).Where(attribute => attribute.Key != "{OriginalFormat}").Select(attribute => (attribute.Key, attribute.Value?.ToString())).Order());
        Assert.DoesNotContain(telemetry.Logs, log => (log.FormattedMessage ?? "").Contains(LinkingFakes.D.IdNumber, StringComparison.Ordinal));

        await browser.StartAcceptedAsync(LinkingFakes.D);

        await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded);
        Assert.Single(ports.Sms.Received);
    }
}
