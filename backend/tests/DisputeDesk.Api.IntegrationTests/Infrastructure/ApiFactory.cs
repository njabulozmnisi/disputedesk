using System.Net;
using DisputeDesk.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The real Api pipeline against a given database (none when null), in the given environment, with optional
/// extra configuration settings (as environment variables would supply them). Zitadel is always the
/// <see cref="FakeZitadel"/>, whose operations machine user is mapped to the operations actor; the local http cookie
/// settings apply unless <paramref name="settings"/> overrides them.
/// </summary>
public sealed class ApiFactory(
    string? connectionString,
    string environment = "Development",
    Action<IServiceCollection>? configureServices = null,
    IReadOnlyDictionary<string, string?>? settings = null,
    TimeProvider? clock = null) : WebApplicationFactory<Program>
{
    public const string SessionCookieName = "disputedesk-session";
    public const string AntiforgeryCookieName = "disputedesk-antiforgery";

    private static readonly Dictionary<string, string?> DefaultSettings = new()
    {
        ["Zitadel:Issuer"] = FakeZitadel.Issuer,
        ["Zitadel:Bff:ClientId"] = FakeZitadel.ClientId,
        ["Zitadel:Bff:ClientSecret"] = FakeZitadel.ClientSecret,
        ["Zitadel:ProjectId"] = FakeZitadel.ProjectId,
        [$"Operations:Actors:{FakeZitadel.OperationsSub}"] = "operations",
        ["Bff:SessionCookie:Name"] = SessionCookieName,
        ["Bff:AntiforgeryCookie:Name"] = AntiforgeryCookieName,
        ["Bff:Cookie:SecurePolicy"] = "SameAsRequest",
        // Nothing listens on port 1. Tests that reach core banking or send an SMS set their own address.
        ["CoreBanking:BaseUrl"] = "http://127.0.0.1:1",
        ["Sms:Host"] = "127.0.0.1",
        ["Sms:Port"] = "1",
    };

    /// <summary>Turns Scalar and the runtime OpenAPI document on; <c>appsettings.json</c> leaves them off (AD-26).</summary>
    public static IReadOnlyDictionary<string, string?> ApiReferenceOn { get; } = new Dictionary<string, string?> { ["Scalar:Enabled"] = "true" };

    public FakeZitadel Zitadel { get; } = new();

    /// <summary>
    /// A browser-like client on https://localhost: it keeps cookies in <paramref name="cookies"/> (share one to
    /// replay a session on another Api instance) and does not follow redirects, so tests can read each hop. HTTPS
    /// because the OpenID Connect handler's correlation cookie is always Secure, and a cookie container only
    /// returns Secure cookies over HTTPS (browsers make an exception for http://localhost).
    /// </summary>
    public HttpClient CreateBrowser(CookieContainer cookies) => CreateDefaultClient(BaseAddress, new CookieContainerHandler(cookies));

    public static Uri BaseAddress { get; } = new("https://localhost");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        if (connectionString is not null)
        {
            builder.UseSetting($"ConnectionStrings:{DependencyInjection.ConnectionStringName}", connectionString);
        }

        foreach ((string key, string? value) in DefaultSettings)
        {
            builder.UseSetting(key, value);
        }

        foreach ((string key, string? value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            Zitadel.AddTo(services);
            if (clock is not null)
            {
                services.AddSingleton(clock);
            }

            configureServices?.Invoke(services);
        });
    }
}
