using System.Globalization;
using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.Linking.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.IntegrationTests.Linking.RateLimits;

/// <summary>6.3 AC5: <c>Linking:RateLimits</c> is checked when the Api starts, and defaults to the production limits.</summary>
public class RateLimitOptionsTests(PostgresFixture postgres)
{
    private const string Permits = "must be from 1 to 1000.";

    [Theory]
    [InlineData("Linking:RateLimits:PermitsPerLogin", "0", Permits)]
    [InlineData("Linking:RateLimits:PermitsPerLogin", "1001", Permits)]
    [InlineData("Linking:RateLimits:PermitsPerIp", "0", Permits)]
    [InlineData("Linking:RateLimits:PermitsPerIp", "1001", Permits)]
    [InlineData("Linking:RateLimits:Window", "00:00:00", "must be more than 0 and at most 1 hour.")]
    [InlineData("Linking:RateLimits:Window", "-00:15:00", "must be more than 0 and at most 1 hour.")]
    [InlineData("Linking:RateLimits:Window", "01:00:01", "must be more than 0 and at most 1 hour.")]
    [InlineData("Linking:RateLimits:SegmentsPerWindow", "0", "must be from 1 to 60 and split the window into whole seconds.")]
    [InlineData("Linking:RateLimits:SegmentsPerWindow", "61", "must be from 1 to 60 and split the window into whole seconds.")]
    // 900 seconds in 7 segments, and in 16 (56.25 s each), are not whole seconds.
    [InlineData("Linking:RateLimits:SegmentsPerWindow", "7", "must be from 1 to 60 and split the window into whole seconds.")]
    [InlineData("Linking:RateLimits:SegmentsPerWindow", "16", "must be from 1 to 60 and split the window into whole seconds.")]
    public void ApiStart_OutOfBounds_StopsTheApiWithAnErrorNamingTheKey(string key, string value, string expected)
    {
        using var factory = new ApiFactory(connectionString: null, settings: new Dictionary<string, string?> { [key] = value });

        Exception refused = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains($"{key} {expected}", refused.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1", "1", "00:00:01", "1")]
    [InlineData("1000", "1000", "01:00:00", "60")]
    public async Task ApiStart_AtTheBounds_StartsWithThoseValues(string perLogin, string perIp, string window, string segments)
    {
        await using var factory = new ApiFactory(
            await postgres.CreateMigratedDatabaseAsync(),
            settings: new Dictionary<string, string?>
            {
                ["Linking:RateLimits:PermitsPerLogin"] = perLogin,
                ["Linking:RateLimits:PermitsPerIp"] = perIp,
                ["Linking:RateLimits:Window"] = window,
                ["Linking:RateLimits:SegmentsPerWindow"] = segments,
            });

        LinkingRateLimitOptions options = factory.Services.GetRequiredService<IOptions<LinkingRateLimitOptions>>().Value;

        Assert.Equal(
            (int.Parse(perLogin, CultureInfo.InvariantCulture), int.Parse(perIp, CultureInfo.InvariantCulture), TimeSpan.Parse(window, CultureInfo.InvariantCulture), int.Parse(segments, CultureInfo.InvariantCulture)),
            (options.PermitsPerLogin, options.PermitsPerIp, options.Window, options.SegmentsPerWindow));
    }

    [Fact]
    public void Defaults_InTheOptionsAndInAppsettings_AreTheProductionLimits()
    {
        // ApiFactory raises the permits for the other suites, so the Api's own settings are read from the file.
        IConfiguration appsettings = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot.Backend, "src", "DisputeDesk.Api", "appsettings.json"))
            .Build();
        LinkingRateLimitOptions configured = appsettings.GetSection(LinkingRateLimitOptions.SectionName).Get<LinkingRateLimitOptions>()
            ?? throw new InvalidOperationException("appsettings.json has no Linking:RateLimits section.");
        var defaults = new LinkingRateLimitOptions();

        foreach (LinkingRateLimitOptions options in new[] { configured, defaults })
        {
            Assert.Equal((5, 20, TimeSpan.FromMinutes(15), 15), (options.PermitsPerLogin, options.PermitsPerIp, options.Window, options.SegmentsPerWindow));
        }
    }

    [Fact]
    public async Task Rejected_WithTheProductionWindow_AnswersRetryAfter900()
    {
        await using RateLimitApi api = await RateLimitApi.StartAsync(postgres, window: "00:15:00", segments: "15");
        using HttpClient browser = await api.Factory.SignInUnlinkedAsync(LinkingApi.NewLogin("retry-after"));
        for (int request = 0; request < 5; request++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await RateLimitApi.MalformedVerifyAsync(browser)).StatusCode);
        }

        HttpResponseMessage rejected = await RateLimitApi.MalformedVerifyAsync(browser);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(["900"], rejected.Headers.GetValues("Retry-After"));
    }
}
