using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// Starting linking as a browser would: an unlinked login, its session cookie and the antiforgery header. Each Api runs
/// on its own freshly migrated database, with <see cref="LinkingFakes"/> for the external systems and a
/// <see cref="TestClock"/>.
/// </summary>
public static class LinkingApi
{
    public const string StartPath = "/api/v1/linking/start";

    private static readonly TimeSpan JobTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The Api with the fakes of <paramref name="ports"/>, the clock <paramref name="clock"/> and any extra settings.</summary>
    public static async Task<ApiFactory> StartApiAsync(
        PostgresFixture postgres,
        LinkingFakes.Ports ports,
        TimeProvider clock,
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string?>? settings = null) =>
        new(
            await postgres.CreateMigratedDatabaseAsync(),
            configureServices: services =>
            {
                ports.AddTo(services);
                configureServices?.Invoke(services);
            },
            settings: settings,
            clock: clock);

    /// <summary>A browser signed in as <paramref name="loginSub"/>, which has no link.</summary>
    public static async Task<HttpClient> SignInUnlinkedAsync(this ApiFactory factory, string loginSub)
    {
        HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, loginSub);
        return browser;
    }

    /// <summary>The JSON body of a start with this customer's details.</summary>
    public static string Body(LinkingFakes.Customer customer) =>
        JsonSerializer.Serialize(new { idNumber = customer.IdNumber, accountNumber = customer.AccountNumber });

    /// <summary>POSTs <paramref name="json"/> to the start, with a fresh antiforgery token unless told not to.</summary>
    public static async Task<HttpResponseMessage> StartLinkingAsync(
        this HttpClient browser, string json, bool withAntiforgeryToken = true, Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(StartPath, UriKind.Relative))
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

    /// <summary>POSTs a start with <paramref name="customer"/>'s details, which must be accepted.</summary>
    public static async Task StartAcceptedAsync(this HttpClient browser, LinkingFakes.Customer customer)
    {
        HttpResponseMessage response = await browser.StartLinkingAsync(Body(customer));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    /// <summary>The login's challenges, oldest first.</summary>
    public static async Task<List<LinkingChallenge>> ChallengesAsync(this ApiFactory factory, string loginSub)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<LinkingChallenge>().AsNoTracking()
            .Where(challenge => challenge.LoginSub == loginSub)
            .OrderBy(challenge => challenge.CreatedAtUtc).ThenBy(challenge => challenge.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The login's linking audit entries, oldest first, the starts' own entries first among those of one instant: a test
    /// clock stamps them all alike (sign-in and sign-out left out).
    /// </summary>
    public static async Task<List<AuditLogEntry>> LinkingAuditsAsync(this ApiFactory factory, string loginSub)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditLogEntry>().AsNoTracking()
            .Where(entry => entry.ActorId == loginSub && entry.Action != AuditAction.SignIn && entry.Action != AuditAction.SignOut)
            .OrderBy(entry => entry.OccurredAtUtc)
            .ThenBy(entry => entry.Action == AuditAction.LinkingStart && entry.Result == AuditResult.Succeeded ? 0 : 1)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Waits until the login has <paramref name="count"/> audit entries of <paramref name="action"/> with
    /// <paramref name="result"/>, which a job writes as its last step, and returns the login's linking audit entries.
    /// </summary>
    public static async Task<List<AuditLogEntry>> WaitForAuditAsync(
        this ApiFactory factory, string loginSub, AuditAction action, AuditResult result, int count = 1)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            List<AuditLogEntry> entries = await factory.LinkingAuditsAsync(loginSub);
            if (entries.Count(entry => entry.Action == action && entry.Result == result) >= count)
            {
                return entries;
            }

            if (deadline.Elapsed > JobTimeout)
            {
                throw new TimeoutException($"No {count} {action} {result} entries within {JobTimeout}; found {string.Join(", ", entries.Select(entry => $"{entry.Action} {entry.Result}"))}.");
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>A new login <c>sub</c> unique to the test.</summary>
    public static string NewLogin(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
