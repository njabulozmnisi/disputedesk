using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>Verifying an OTP as a browser would, and reading what verification wrote.</summary>
public static class VerifyApi
{
    public const string VerifyPath = "/api/v1/linking/verify";

    /// <summary>The audit actions a verification records.</summary>
    public static readonly AuditAction[] VerifyActions =
        [AuditAction.LinkingOtpIncorrect, AuditAction.LinkingOtpExpired, AuditAction.LinkingLocked, AuditAction.Linked];

    public static string Body(string code) => JsonSerializer.Serialize(new { code });

    /// <summary>POSTs <paramref name="json"/> to the verify route, with a fresh antiforgery token unless told not to.</summary>
    public static async Task<HttpResponseMessage> VerifyAsync(
        this HttpClient browser, string json, bool withAntiforgeryToken = true, Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(VerifyPath, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (withAntiforgeryToken)
        {
            request.Headers.Add("X-XSRF-TOKEN", await browser.AntiforgeryTokenAsync());
        }

        configure?.Invoke(request);
        return await browser.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static Task<HttpResponseMessage> VerifyCodeAsync(this HttpClient browser, string code) => browser.VerifyAsync(Body(code));

    /// <summary>
    /// Starts linking with <paramref name="customer"/>'s details, waits for the job to send the OTP and returns it: the
    /// latest message to that customer's cellphone.
    /// </summary>
    public static async Task<string> StartAndReadOtpAsync(
        this ApiFactory factory, HttpClient browser, LinkingFakes.Ports ports, string login, LinkingFakes.Customer customer)
    {
        int sentBefore = await factory.CountAuditsAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded);
        await browser.StartAcceptedAsync(customer);
        await factory.WaitForAuditAsync(login, AuditAction.LinkingOtpSent, AuditResult.Succeeded, sentBefore + 1);
        string message = ports.Sms.Received.Last(received => received.Cellphone == customer.Cellphone).Message;
        return System.Text.RegularExpressions.Regex.Match(message, "[0-9]{6}").Value;
    }

    /// <summary>A six-digit code that is not <paramref name="otp"/>.</summary>
    public static string WrongCode(string otp) =>
        ((int.Parse(otp, CultureInfo.InvariantCulture) + 1) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public static async Task<int> CountAuditsAsync(this ApiFactory factory, string login, AuditAction action, AuditResult result) =>
        (await factory.LinkingAuditsAsync(login)).Count(entry => entry.Action == action && entry.Result == result);

    /// <summary>The login's verification audit entries, oldest first.</summary>
    public static async Task<List<AuditLogEntry>> VerifyAuditsAsync(this ApiFactory factory, string login) =>
        [.. (await factory.LinkingAuditsAsync(login)).Where(entry => VerifyActions.Contains(entry.Action))];

    /// <summary>The login's current challenge: neither superseded nor consumed.</summary>
    public static async Task<LinkingChallenge?> CurrentChallengeAsync(this ApiFactory factory, string login) =>
        (await factory.ChallengesAsync(login)).SingleOrDefault(challenge => challenge.SupersededAtUtc is null && challenge.ConsumedAtUtc is null);

    public static async Task<List<CustomerLink>> LinksAsync(this ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<CustomerLink>().AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>422 <c>otp-incorrect</c> with <paramref name="attemptsRemaining"/>.</summary>
    public static async Task AssertOtpIncorrectAsync(this HttpResponseMessage response, int attemptsRemaining)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(LinkingProblems.OtpIncorrect), problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(attemptsRemaining, problem.RootElement.GetProperty("attemptsRemaining").GetInt32());
    }

    /// <summary>409 <c>linking-locked</c>.</summary>
    public static async Task AssertLockedAsync(this HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemType.For(LinkingProblems.LinkingLocked), await response.ProblemTypeAsync());
    }

    /// <summary>The status and the problem body without its <c>traceId</c>, the one value that differs per request.</summary>
    public static async Task<string> StatusAndBodyAsync(this HttpResponseMessage response) =>
        $"{(int)response.StatusCode} {await response.BodyWithoutTraceIdAsync()}";

    /// <summary>
    /// The tags of each <c>disputedesk.linking.events</c> measurement as <c>name=value</c>, in order; every measurement
    /// must add 1, so each is one event.
    /// </summary>
    public static List<string> Events(this MetricCollector<long> linkingEvents) =>
    [
        .. linkingEvents.GetMeasurementSnapshot().Select(measurement =>
        {
            Assert.Equal(1, measurement.Value);
            return string.Join(',', measurement.SortedTags().Select(tag => $"{tag.Name}={tag.Value}"));
        }),
    ];
}
