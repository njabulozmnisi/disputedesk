using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.2 AC8: <c>disputedesk.linking.events</c> counts <c>locked</c> once per challenge and <c>completed</c> once per
/// link, labelled only by the event; a failed save of a failure entry leaves the answer unchanged and logs only the action.
/// The audit entry of each outcome is asserted where the outcome is (the attempt, kind, link and race tests).
/// </summary>
public class VerifyOtpAuditTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Verify_LockingThenLinking_CountsLockedOncePerChallengeAndCompletedOncePerLink()
    {
        var ports = new LinkingFakes.Ports();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock());
        var events = new ConcurrentQueue<string>();
        using MeterListener listener = factory.ListenToLinkingEvents(events);
        string locked = LinkingApi.NewLogin("metric-locked");
        using HttpClient lockedBrowser = await factory.SignInUnlinkedAsync(locked);
        string wrong = VerifyApi.WrongCode(await factory.StartAndReadOtpAsync(lockedBrowser, ports, locked, LinkingFakes.D));
        string linked = LinkingApi.NewLogin("metric-linked");
        using HttpClient linkedBrowser = await factory.SignInUnlinkedAsync(linked);
        string otp = await factory.StartAndReadOtpAsync(linkedBrowser, ports, linked, LinkingFakes.D);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await lockedBrowser.VerifyCodeAsync(wrong);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await linkedBrowser.VerifyCodeAsync(otp)).StatusCode);

        Assert.Equal(["event=locked", "event=completed"], events.Where(tags => tags != "event=started"));
    }

    [Fact]
    public async Task Verify_FailureEntrySaveFails_AnswersUnchangedAndLogsOnlyTheAction()
    {
        var ports = new LinkingFakes.Ports();
        var failAudit = new FailAuditInsertInterceptor { Armed = false };
        var telemetry = new TelemetryCapture();
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, ports, new TestClock(), services =>
        {
            telemetry.AddTo(services);
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failAudit));
        });
        string login = LinkingApi.NewLogin("verify-audit-fails");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);
        string otp = await factory.StartAndReadOtpAsync(browser, ports, login, LinkingFakes.D);
        failAudit.Armed = true;

        HttpResponseMessage response = await browser.VerifyCodeAsync(VerifyApi.WrongCode(otp));
        failAudit.Armed = false;

        await response.AssertOtpIncorrectAsync(attemptsRemaining: 2);
        Assert.Equal(1, failAudit.Failed);
        Assert.Equal(1, (await factory.CurrentChallengeAsync(login))?.Attempts);
        LogRecord error = Assert.Single(telemetry.Logs, log => log.LogLevel == LogLevel.Error && log.CategoryName == "DisputeDesk.Infrastructure.Linking.VerifyOtpHandler");
        Assert.Null(error.Exception);
        Assert.Equal(
            [("Action", nameof(AuditAction.LinkingOtpIncorrect))],
            (error.Attributes ?? []).Where(attribute => attribute.Key != "{OriginalFormat}").Select(attribute => (attribute.Key, attribute.Value?.ToString())));
    }
}
