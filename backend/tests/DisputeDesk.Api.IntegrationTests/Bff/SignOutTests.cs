using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Infrastructure.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using OpenTelemetry.Logs;

namespace DisputeDesk.Api.IntegrationTests.Bff;

/// <summary>
/// 2.2 AC6 and 2.8 AC5 and AC6: signing out ends the DisputeDesk session, records <c>SignOut</c>, and the OpenID
/// Connect handler sends the browser to end the Zitadel one (RP-Initiated Logout 1.0).
/// </summary>
public class SignOutTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Logout_SignedIn_DeletesTheRowClearsBothCookiesAuditsAndRedirectsToEndSession()
    {
        var clock = new TestClock();
        await using var factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync(), clock: clock);
        await factory.LinkAsync("login-signout", "CUS-000001");
        var cookies = new CookieContainer();
        using HttpClient browser = factory.CreateBrowser(cookies);
        await browser.SignInAsync(factory.Zitadel, "login-signout");
        string token = await browser.AntiforgeryTokenAsync();
        Assert.Contains(cookies.GetAllCookies(), cookie => cookie.Name == ApiFactory.AntiforgeryCookieName);

        HttpResponseMessage response = await browser.LogoutAsync(token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Uri endSession = Assert.IsType<Uri>(response.Headers.Location);

        Assert.DoesNotContain(cookies.GetAllCookies(), cookie => cookie.Name == ApiFactory.SessionCookieName);
        Assert.DoesNotContain(cookies.GetAllCookies(), cookie => cookie.Name == ApiFactory.AntiforgeryCookieName);
        Assert.Empty(await SessionRowsAsync(factory));

        AuditLogEntry signOut = Assert.Single(await AuditEntriesAsync(factory), entry => entry.Action == AuditAction.SignOut);
        Assert.Equal(AuditResult.Succeeded, signOut.Result);
        Assert.Equal(ActorType.Customer, signOut.ActorType);
        Assert.Equal("login-signout", signOut.ActorId);
        Assert.Equal("CUS-000001", signOut.CustomerId);
        Assert.Equal(clock.NowAsStored, signOut.OccurredAtUtc);

        // The browser follows the redirect to Zitadel, which checks the request and sends it back to the signed-out
        // callback with the same state; the handler lands it on /.
        Uri signedOut = factory.Zitadel.EndSession(endSession);
        Assert.Equal(QueryHelpers.ParseQuery(endSession.Query)["state"], QueryHelpers.ParseQuery(signedOut.Query).GetValueOrDefault("state"));
        HttpResponseMessage callback = await browser.GetPathAsync(signedOut.PathAndQuery);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/", callback.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Request_ReplayingTheCookieAfterLogout_Returns401()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        var cookies = new CookieContainer();
        using HttpClient browser = factory.CreateBrowser(cookies);
        await browser.SignInAsync(factory.Zitadel);
        Cookie oldSession = cookies.GetAllCookies().Single(cookie => cookie.Name == ApiFactory.SessionCookieName);
        await browser.LogoutAsync(await browser.AntiforgeryTokenAsync());

        HttpResponseMessage response = await ReplayAsync(factory, oldSession);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_Anonymous_SkipsAntiforgeryAuditsNothingAndRedirectsToEndSessionWithAStateAndNoHint()
    {
        await using var factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync());
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage response = await browser.LogoutAsync(antiforgeryToken: null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Uri endSession = Assert.IsType<Uri>(response.Headers.Location);
        Assert.StartsWith(factory.Zitadel.Configuration.EndSessionEndpoint, endSession.ToString(), StringComparison.Ordinal);
        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(endSession.Query);
        Assert.False(query.ContainsKey("id_token_hint"));
        Assert.False(string.IsNullOrEmpty(query["state"].ToString()));
        Assert.Empty(await AuditEntriesAsync(factory));
    }

    [Fact]
    public async Task Logout_SignOutAuditSaveFails_StillEndsTheSessionAndLogsAnErrorWithoutTokensOrEmail()
    {
        // 2.8 AC6: logout must end the session (AD-7), so a failed SignOut entry is logged, not fatal.
        var failAudit = new FailAuditInsertInterceptor();
        var telemetry = new TelemetryCapture();
        await using var factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync(), configureServices: services =>
        {
            telemetry.AddTo(services);
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failAudit));
        });
        var cookies = new CookieContainer();
        using HttpClient browser = factory.CreateBrowser(cookies);
        // Armed only after sign-in, so the SignIn entry is saved and only the SignOut entry fails.
        failAudit.Armed = false;
        await browser.SignInAsync(factory.Zitadel, "login-audit-fails");
        string idToken = factory.Zitadel.LastIdToken ?? throw new InvalidOperationException("No ID token was issued.");
        string token = await browser.AntiforgeryTokenAsync();
        failAudit.Armed = true;

        HttpResponseMessage response = await browser.LogoutAsync(token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(factory.Zitadel.Configuration.EndSessionEndpoint, response.Headers.Location?.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, failAudit.Failed);
        Assert.DoesNotContain(cookies.GetAllCookies(), cookie => cookie.Name == ApiFactory.SessionCookieName);
        Assert.DoesNotContain(cookies.GetAllCookies(), cookie => cookie.Name == ApiFactory.AntiforgeryCookieName);
        Assert.Empty(await SessionRowsAsync(factory));
        Assert.DoesNotContain(await AuditEntriesAsync(factory), entry => entry.Action == AuditAction.SignOut);

        LogRecord error = Assert.Single(telemetry.Logs, log => log.LogLevel == LogLevel.Error && log.CategoryName == "DisputeDesk.Api.Bff.BffEndpoints");
        string[] secrets = [idToken, "opaque-access-token", "login-audit-fails@disputedesk.test"];
        string logged = string.Join('\n', new[] { error.FormattedMessage, error.Body, error.Exception?.ToString() }
            .Concat((error.Attributes ?? []).Select(attribute => attribute.Value?.ToString())));
        Assert.DoesNotContain(secrets, secret => logged.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_AlreadySignedIn_RecordsSignOutForTheEndedSessionBeforeTheNewSignIn()
    {
        // A second sign-in ends the first session (AD-7), so it is audited as that session's sign-out.
        var clock = new TestClock();
        await using var factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync(), clock: clock);
        await factory.LinkAsync("login-first", "CUS-000001");
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "login-first");

        await browser.SignInAsync(factory.Zitadel, "login-second");

        List<AuditLogEntry> entries = await AuditEntriesAsync(factory);
        Assert.Equal(3, entries.Count);
        AuditLogEntry signOut = Assert.Single(entries, entry => entry.Action == AuditAction.SignOut);
        Assert.Equal(AuditResult.Succeeded, signOut.Result);
        Assert.Equal(ActorType.Customer, signOut.ActorType);
        Assert.Equal("login-first", signOut.ActorId);
        Assert.Equal("CUS-000001", signOut.CustomerId);
        Assert.Equal(clock.NowAsStored, signOut.OccurredAtUtc);
        Assert.Equal(["login-first", "login-second"], entries.Where(entry => entry.Action == AuditAction.SignIn).Select(entry => entry.ActorId).Order());
    }

    [Fact]
    public async Task Login_AlreadySignedInAndSignOutAuditSaveFails_StillEndsTheSessionLogsAnErrorAndRedirectsToZitadel()
    {
        // The same rule as logout (AD-7): a failed SignOut entry is logged, not fatal.
        var failAudit = new FailAuditInsertInterceptor();
        var telemetry = new TelemetryCapture();
        await using var factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync(), configureServices: services =>
        {
            telemetry.AddTo(services);
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failAudit));
        });
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        failAudit.Armed = false;
        await browser.SignInAsync(factory.Zitadel, "login-relogin-fails");
        failAudit.Armed = true;

        HttpResponseMessage login = await browser.GetPathAsync("/api/v1/bff/login");

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.StartsWith(factory.Zitadel.Configuration.AuthorizationEndpoint, login.Headers.Location?.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, failAudit.Failed);
        Assert.Empty(await SessionRowsAsync(factory));
        Assert.DoesNotContain(await AuditEntriesAsync(factory), entry => entry.Action == AuditAction.SignOut);
        Assert.Single(telemetry.Logs, log => log.LogLevel == LogLevel.Error && log.CategoryName == "DisputeDesk.Api.Bff.BffEndpoints");
    }

    [Fact]
    public async Task Logout_SessionRowDeletionFails_Returns500AndKeepsTheLoginSignedInWithNoSignOutEntry()
    {
        await using var factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync(), configureServices: services =>
            services.PostConfigure<CookieAuthenticationOptions>(
                CookieAuthenticationDefaults.AuthenticationScheme,
                cookie => cookie.SessionStore = new FailingRemoveStore(cookie.SessionStore ?? throw new InvalidOperationException("No session store."))));
        var cookies = new CookieContainer();
        using HttpClient browser = factory.CreateBrowser(cookies);
        await browser.SignInAsync(factory.Zitadel, "login-delete-fails");
        string token = await browser.AntiforgeryTokenAsync();

        HttpResponseMessage response = await browser.LogoutAsync(token);

        await AssertInternalProblemAsync(response);
        Assert.Contains(cookies.GetAllCookies(), cookie => cookie.Name == ApiFactory.SessionCookieName);
        Assert.Contains(cookies.GetAllCookies(), cookie => cookie.Name == ApiFactory.AntiforgeryCookieName);
        Assert.Single(await SessionRowsAsync(factory));
        Assert.DoesNotContain(await AuditEntriesAsync(factory), entry => entry.Action == AuditAction.SignOut);
        Assert.Equal("unlinked", await browser.SessionStateAsync());
    }

    [Fact]
    public async Task Logout_DiscoveryFails_Returns500AfterEndingTheLocalSession()
    {
        await using var factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync());
        var cookies = new CookieContainer();
        using HttpClient browser = factory.CreateBrowser(cookies);
        await browser.SignInAsync(factory.Zitadel, "login-discovery-fails");
        Cookie oldSession = cookies.GetAllCookies().Single(cookie => cookie.Name == ApiFactory.SessionCookieName);
        string token = await browser.AntiforgeryTokenAsync();
        factory.Zitadel.DiscoveryFails = true;

        HttpResponseMessage response = await browser.LogoutAsync(token);

        await AssertInternalProblemAsync(response);
        Assert.Empty(await SessionRowsAsync(factory));
        Assert.Equal(HttpStatusCode.Unauthorized, (await ReplayAsync(factory, oldSession)).StatusCode);
    }

    private static async Task<HttpResponseMessage> ReplayAsync(ApiFactory factory, Cookie oldSession)
    {
        // An attacker who copied the cookie before sign-out sends it again.
        var replayed = new CookieContainer();
        replayed.Add(ApiFactory.BaseAddress, new Cookie(oldSession.Name, oldSession.Value, "/"));
        using HttpClient replay = factory.CreateBrowser(replayed);
        return await replay.GetPathAsync("/api/v1/bff/antiforgery");
    }

    private static async Task AssertInternalProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(CommonProblems.Internal), problem.RootElement.GetProperty("type").GetString());
    }

    private static async Task<List<Session>> SessionRowsAsync(ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Session>().AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<List<AuditLogEntry>> AuditEntriesAsync(ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditLogEntry>().AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The real session store, except that deleting a session fails, as a database outage would.</summary>
    private sealed class FailingRemoveStore(ITicketStore inner) : ITicketStore
    {
        public Task<string> StoreAsync(AuthenticationTicket ticket) => inner.StoreAsync(ticket);

        public Task RenewAsync(string key, AuthenticationTicket ticket) => inner.RenewAsync(key, ticket);

        public Task<AuthenticationTicket?> RetrieveAsync(string key) => inner.RetrieveAsync(key);

        public Task RemoveAsync(string key) => throw new InvalidOperationException("The session row could not be deleted.");
    }
}
