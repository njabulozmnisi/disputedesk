using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using DisputeDesk.Api.Bff;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Infrastructure.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using OpenTelemetry.Logs;

namespace DisputeDesk.Api.IntegrationTests.Bff;

/// <summary>
/// 2.2 AC1 and AC2: code flow with PKCE, a server-side session behind a key-only cookie, and the email check. 2.8 AC1 to
/// AC4: every completed sign-in is audited through <c>AuditLog.Record</c>, and a failed audit stops it.
/// </summary>
public class SignInTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Login_Anonymous_RedirectsToZitadelWithCodeFlowAndPkce()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage login = await browser.GetPathAsync("/api/v1/bff/login");

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Uri authorize = Assert.IsType<Uri>(login.Headers.Location);
        Assert.StartsWith(factory.Zitadel.Configuration.AuthorizationEndpoint, authorize.ToString(), StringComparison.Ordinal);
        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(authorize.Query);
        Assert.Equal("code", query["response_type"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.False(string.IsNullOrEmpty(query["code_challenge"].ToString()));
        Assert.Equal(FakeZitadel.ClientId, query["client_id"].ToString());
        Assert.Equal("https://localhost/api/signin-oidc", query["redirect_uri"].ToString());
    }

    [Fact]
    public async Task Callback_VerifiedLogin_SetsOnlyAnHttpOnlyStrictSessionCookieAndStoresTheTicketByKeyHash()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        var cookies = new CookieContainer();
        using HttpClient browser = factory.CreateBrowser(cookies);
        string sub = $"login-{Guid.NewGuid():N}";

        HttpResponseMessage callback = await browser.SignInAsync(factory.Zitadel, sub, returnUrl: "/disputes");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/disputes", callback.Headers.Location?.ToString());
        string setCookie = Assert.Single(callback.Headers.GetValues("Set-Cookie"), value => value.StartsWith(ApiFactory.SessionCookieName + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        // No token reaches the browser: not in the redirect, not in any cookie, not in the body. The exact tokens the fake
        // issued, not a JWT-looking substring, which the random Data Protection ciphertext in the cookie can contain.
        string idToken = factory.Zitadel.LastIdToken ?? throw new InvalidOperationException("No ID token was issued.");
        string everythingSent = string.Join('\n', callback.Headers.SelectMany(header => header.Value))
            + await callback.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("opaque-access-token", everythingSent, StringComparison.Ordinal);
        Assert.DoesNotContain(idToken, everythingSent, StringComparison.Ordinal);

        // The row holds the SHA-256 of a random key and the login; the ticket in it is data-protected.
        await using AppDbContext db = postgres.CreateDbContext();
        Session session = await db.Set<Session>().SingleAsync(row => row.LoginSub == sub, TestContext.Current.CancellationToken);
        Assert.Equal(32, session.KeyHash.Length);
        Assert.DoesNotContain(sub, Encoding.UTF8.GetString(session.Ticket), StringComparison.Ordinal);
        Assert.Equal("unlinked", await browser.SessionStateAsync());
    }

    [Fact]
    public async Task Callback_VerifiedLogin_KeepsOnlyTheIdTokenInTheProtectedTicket()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        string sub = $"tokens-{Guid.NewGuid():N}";

        await browser.SignInAsync(factory.Zitadel, sub);

        await using AppDbContext db = postgres.CreateDbContext();
        Session session = await db.Set<Session>().SingleAsync(row => row.LoginSub == sub, TestContext.Current.CancellationToken);
        IDataProtector protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(SessionTicketStore.TicketProtectorPurpose);
        AuthenticationTicket? ticket = TicketSerializer.Default.Deserialize(protector.Unprotect(session.Ticket));
        AuthenticationToken token = Assert.Single(ticket?.Properties.GetTokens() ?? []);
        Assert.Equal("id_token", token.Name);
        Assert.Equal(factory.Zitadel.LastIdToken, token.Value);
        Assert.Null(ticket?.Properties.GetTokenValue("access_token"));
    }

    [Fact]
    public async Task Callback_TokenRequest_SendsThePkceVerifierAndTheClientSecret()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());

        await browser.SignInAsync(factory.Zitadel);

        IReadOnlyDictionary<string, string> form = factory.Zitadel.LastTokenRequest;
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.False(string.IsNullOrEmpty(form["code_verifier"]));
        Assert.Equal(FakeZitadel.ClientId, form["client_id"]);
        Assert.Equal(FakeZitadel.ClientSecret, form["client_secret"]);
    }

    [Theory]
    [InlineData("not-the-secret")]
    [InlineData(null)]
    public async Task Callback_WrongOrMissingClientSecret_RedirectsToSignInFailedWithNoSession(string? clientSecret)
    {
        // Zitadel answers 401 invalid_client; the Api sends the browser back to the SPA with no session.
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            configureServices: services => services.PostConfigure<OpenIdConnectOptions>(
                OpenIdConnectDefaults.AuthenticationScheme, oidc => oidc.ClientSecret = clientSecret));
        var cookies = new CookieContainer();
        using HttpClient browser = factory.CreateBrowser(cookies);
        string sub = $"bad-client-{Guid.NewGuid():N}";

        HttpResponseMessage callback = await browser.SignInAsync(factory.Zitadel, sub);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/?signIn=failed", callback.Headers.Location?.ToString());
        Assert.DoesNotContain(cookies.GetAllCookies(), cookie => cookie.Name == ApiFactory.SessionCookieName);
        await using AppDbContext db = postgres.CreateDbContext();
        Assert.False(await db.Set<Session>().AnyAsync(row => row.LoginSub == sub, TestContext.Current.CancellationToken));
        Assert.False(await db.Set<AuditLogEntry>().AnyAsync(entry => entry.ActorId == sub, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Session_CookieFromOneApiInstance_IsAcceptedByAnother()
    {
        // Two Api instances share only the database: the session row and the Data Protection keys live there.
        await using var first = new ApiFactory(postgres.ConnectionString);
        await using var second = new ApiFactory(postgres.ConnectionString);
        var cookies = new CookieContainer();
        using HttpClient firstBrowser = first.CreateBrowser(cookies);
        using HttpClient secondBrowser = second.CreateBrowser(cookies);

        await firstBrowser.SignInAsync(first.Zitadel);

        Assert.Equal("unlinked", await secondBrowser.SessionStateAsync());

        // The key that protected the cookie is in Postgres, not in a key ring both test instances happen to share on
        // disk. A Data Protection payload starts with a 4-byte magic header, then the 16-byte ID of its key.
        string sessionCookie = cookies.GetAllCookies().Single(cookie => cookie.Name == ApiFactory.SessionCookieName).Value;
        var keyId = new Guid(WebEncoders.Base64UrlDecode(sessionCookie).AsSpan(4, 16));
        await using AppDbContext db = postgres.CreateDbContext();
        List<string?> storedKeys = await db.DataProtectionKeys.Select(key => key.Xml).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(storedKeys, xml => xml is not null && xml.Contains($"id=\"{keyId}\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_AlreadySignedIn_EndsTheOldSessionAndStartsANewOneWithANewKey()
    {
        // Session fixation (AD-7): a second sign-in in the same browser must not reuse the first session's key.
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        string firstSub = $"first-{Guid.NewGuid():N}";
        string secondSub = $"second-{Guid.NewGuid():N}";
        await browser.SignInAsync(factory.Zitadel, firstSub);
        byte[] firstKeyHash = await KeyHashOfAsync(firstSub);

        await browser.SignInAsync(factory.Zitadel, secondSub);

        await using AppDbContext db = postgres.CreateDbContext();
        Assert.False(await db.Set<Session>().AnyAsync(row => row.LoginSub == firstSub, TestContext.Current.CancellationToken));
        Assert.NotEqual(firstKeyHash, await KeyHashOfAsync(secondSub));
        Assert.Equal("unlinked", await browser.SessionStateAsync());
    }

    private async Task<byte[]> KeyHashOfAsync(string sub)
    {
        await using AppDbContext db = postgres.CreateDbContext();
        Session session = await db.Set<Session>().SingleAsync(row => row.LoginSub == sub, TestContext.Current.CancellationToken);
        return session.KeyHash;
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    public async Task Callback_NonLocalReturnUrl_RedirectsToRoot(string returnUrl)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage callback = await browser.SignInAsync(factory.Zitadel, returnUrl: returnUrl);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/", callback.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Callback_UnverifiedEmail_RedirectsToSignInFailedWithNoSession()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        var cookies = new CookieContainer();
        using HttpClient browser = factory.CreateBrowser(cookies);
        string sub = $"unverified-{Guid.NewGuid():N}";

        HttpResponseMessage callback = await browser.SignInAsync(factory.Zitadel, sub, emailVerified: false);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/?signIn=failed", callback.Headers.Location?.ToString());
        Assert.DoesNotContain(cookies.GetAllCookies(), cookie => cookie.Name == ApiFactory.SessionCookieName);
        await using AppDbContext db = postgres.CreateDbContext();
        Assert.False(await db.Set<Session>().AnyAsync(row => row.LoginSub == sub, TestContext.Current.CancellationToken));
        Assert.False(await db.Set<AuditLogEntry>().AnyAsync(entry => entry.ActorId == sub, TestContext.Current.CancellationToken));
        Assert.Equal("anonymous", await browser.SessionStateAsync());
    }

    [Fact]
    public async Task Record_GivenItsFields_AddsOneRowWithExactlyThemStampedWithTheCallersInstant()
    {
        // 2.8 AC1: the caller's instant, far from the real clock, so a clock read would show.
        var instant = new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero);
        var disputeId = Guid.CreateVersion7(instant);
        string connectionString = await postgres.CreateMigratedDatabaseAsync();
        using Activity activity = new Activity("audit-record").Start();
        await using (AppDbContext db = CreateDbContext(connectionString))
        {
            AuditLog.Record(db, AuditAction.SignIn, AuditResult.Succeeded, new Actor(ActorType.Customer, "login-a"), instant, "CUS-000001", disputeId);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using AppDbContext read = CreateDbContext(connectionString);
        AuditLogEntry entry = await read.Set<AuditLogEntry>().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(7, entry.Id.Version);
        Assert.Equal(instant.ToUnixTimeMilliseconds(), UnixMillisecondsOf(entry.Id));
        Assert.Equal(instant, entry.OccurredAtUtc);
        Assert.Equal(AuditAction.SignIn, entry.Action);
        Assert.Equal(AuditResult.Succeeded, entry.Result);
        Assert.Equal(ActorType.Customer, entry.ActorType);
        Assert.Equal("login-a", entry.ActorId);
        Assert.Equal("CUS-000001", entry.CustomerId);
        Assert.Equal(disputeId, entry.DisputeId);
        Assert.Equal(activity.TraceId.ToHexString(), entry.TraceId);

        // Both actions are stored as their member names (AD-22).
        AuditLog.Record(read, AuditAction.SignOut, AuditResult.Succeeded, new Actor(ActorType.Customer, "login-a"), instant);
        await read.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["SignIn", "SignOut"], await read.Database.SqlQuery<string>($"SELECT \"Action\" AS \"Value\" FROM \"AuditLogEntries\" ORDER BY \"Action\"").ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Callback_LinkedAndUnlinkedLogins_EachWriteOneSignInEntryWithTheirSubAndLinkedCustomer()
    {
        // 2.8 AC2: the actor is the login; the customer ID comes from CustomerLinks, null when there is no link.
        var clock = new TestClock();
        await using var factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync(), clock: clock);
        await factory.LinkAsync("login-linked", "CUS-000001");
        using HttpClient linked = factory.CreateBrowser(new CookieContainer());
        using HttpClient unlinked = factory.CreateBrowser(new CookieContainer());

        await linked.SignInAsync(factory.Zitadel, "login-linked");
        await unlinked.SignInAsync(factory.Zitadel, "login-unlinked");

        List<AuditLogEntry> entries = await AuditEntriesAsync(factory);
        Assert.Equal(2, entries.Count);
        AuditLogEntry linkedEntry = Assert.Single(entries, entry => entry.ActorId == "login-linked");
        AuditLogEntry unlinkedEntry = Assert.Single(entries, entry => entry.ActorId == "login-unlinked");
        foreach (AuditLogEntry entry in entries)
        {
            Assert.Equal(AuditAction.SignIn, entry.Action);
            Assert.Equal(AuditResult.Succeeded, entry.Result);
            Assert.Equal(ActorType.Customer, entry.ActorType);
            Assert.Equal(clock.NowAsStored, entry.OccurredAtUtc);
            Assert.Null(entry.DisputeId);
            Assert.False(string.IsNullOrEmpty(entry.TraceId));
        }

        Assert.Equal("CUS-000001", linkedEntry.CustomerId);
        Assert.Null(unlinkedEntry.CustomerId);
    }

    [Fact]
    public async Task Request_RenewingTheSession_WritesNoFurtherSignInEntry()
    {
        await using var factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync());
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, "login-renews");

        Assert.Equal("unlinked", await browser.SessionStateAsync());
        Assert.Equal("unlinked", await browser.SessionStateAsync());

        Assert.Single(await AuditEntriesAsync(factory));
    }

    [Fact]
    public async Task Callback_SignInAuditInsertFails_Returns500WithNoSessionRowAndNoSessionCookie()
    {
        // 2.8 AC3: no session exists without its SignIn entry.
        var failAudit = new FailAuditInsertInterceptor();
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failAudit)));
        var cookies = new CookieContainer();
        using HttpClient browser = factory.CreateBrowser(cookies);
        string sub = $"audit-fails-{Guid.NewGuid():N}";

        HttpResponseMessage callback = await browser.SignInAsync(factory.Zitadel, sub);

        Assert.Equal(HttpStatusCode.InternalServerError, callback.StatusCode);
        Assert.Equal("application/problem+json", callback.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = await callback.ReadJsonAsync();
        Assert.Equal(ProblemType.For(CommonProblems.Internal), problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, failAudit.Failed);
        Assert.False(callback.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain(cookies.GetAllCookies(), cookie => cookie.Name == ApiFactory.SessionCookieName);
        await using AppDbContext db = postgres.CreateDbContext();
        Assert.False(await db.Set<Session>().AnyAsync(row => row.LoginSub == sub, TestContext.Current.CancellationToken));
        Assert.Equal("anonymous", await browser.SessionStateAsync());
    }

    [Fact]
    public async Task Callback_VerifiedUnverifiedAndWrongSecretLogins_LogNoTokenOrEmailAndAuditNone()
    {
        // 2.8 AC4: exactly three callbacks, every log record the Api writes for them, and every audit row.
        var telemetry = new TelemetryCapture();
        string connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using var factory = new ApiFactory(connectionString, configureServices: telemetry.AddTo);
        await using var wrongSecret = new ApiFactory(connectionString, configureServices: services =>
        {
            telemetry.AddTo(services);
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, oidc => oidc.ClientSecret = "not-the-secret");
        });
        var secrets = new List<string> { "opaque-access-token" };

        using HttpClient verifiedBrowser = factory.CreateBrowser(new CookieContainer());
        Assert.Equal(HttpStatusCode.Redirect, (await verifiedBrowser.SignInAsync(factory.Zitadel, "login-verified")).StatusCode);
        secrets.AddRange([factory.Zitadel.LastIdToken ?? "", "login-verified@disputedesk.test"]);

        using HttpClient unverifiedBrowser = factory.CreateBrowser(new CookieContainer());
        Assert.Equal("/?signIn=failed", (await unverifiedBrowser.SignInAsync(factory.Zitadel, "login-unverified", emailVerified: false)).Headers.Location?.ToString());
        secrets.AddRange([factory.Zitadel.LastIdToken ?? "", "login-unverified@disputedesk.test"]);

        using HttpClient wrongSecretBrowser = wrongSecret.CreateBrowser(new CookieContainer());
        Assert.Equal("/?signIn=failed", (await wrongSecretBrowser.SignInAsync(wrongSecret.Zitadel, "login-wrong-secret")).Headers.Location?.ToString());
        secrets.Add("login-wrong-secret@disputedesk.test");

        Assert.DoesNotContain("", secrets);
        Assert.Contains(telemetry.Logs, log => log.CategoryName == typeof(BffAuthentication).FullName);
        foreach (string logged in telemetry.Logs.SelectMany(LoggedText))
        {
            Assert.DoesNotContain(secrets, secret => logged.Contains(secret, StringComparison.Ordinal));
        }

        List<AuditLogEntry> entries = await AuditEntriesAsync(factory);
        AuditLogEntry signIn = Assert.Single(entries);
        Assert.Equal("login-verified", signIn.ActorId);
        string audited = string.Join('\n', entries.Select(entry => $"{entry.ActorId}|{entry.CustomerId}|{entry.TraceId}"));
        Assert.DoesNotContain(secrets, secret => audited.Contains(secret, StringComparison.Ordinal));
    }

    private static IEnumerable<string> LoggedText(LogRecord log) =>
        new[] { log.FormattedMessage, log.Body, log.Exception?.ToString() }
            .Concat((log.Attributes ?? []).Select(attribute => attribute.Value?.ToString()))
            .OfType<string>();

    private static AppDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);

    private static async Task<List<AuditLogEntry>> AuditEntriesAsync(ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditLogEntry>().AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    // A UUIDv7 starts with the 48-bit Unix time in milliseconds (RFC 9562, section 5.7).
    private static long UnixMillisecondsOf(Guid id)
    {
        byte[] bytes = id.ToByteArray(bigEndian: true);
        return ((long)bytes[0] << 40) | ((long)bytes[1] << 32) | ((long)bytes[2] << 24) | ((long)bytes[3] << 16) | ((long)bytes[4] << 8) | bytes[5];
    }

    [Fact]
    public async Task Callback_AwsCookieSettingsOverHttps_SetsASecureHostPrefixedCookie()
    {
        // The AWS values from appsettings.json (AD-24): __Host- names and SecurePolicy Always.
        await using var factory = new ApiFactory(postgres.ConnectionString, settings: new Dictionary<string, string?>
        {
            ["Bff:SessionCookie:Name"] = "__Host-disputedesk-session",
            ["Bff:AntiforgeryCookie:Name"] = "__Host-disputedesk-antiforgery",
            ["Bff:Cookie:SecurePolicy"] = "Always",
        });
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage callback = await browser.SignInAsync(factory.Zitadel);

        string setCookie = Assert.Single(callback.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-disputedesk-session=", StringComparison.Ordinal));
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
    }
}
