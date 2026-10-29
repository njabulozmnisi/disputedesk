using System.Data.Common;
using System.Net;
using DisputeDesk.Api.Bff;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Infrastructure.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Bff;

/// <summary>AC3: 15 minutes idle or 12 hours in total ends the session; every authenticated request renews the idle limit.</summary>
public class SessionExpiryTests(PostgresFixture postgres)
{
    // GET /api/v1/bff/antiforgery needs a session, so it answers 401 once the session is gone.
    private const string AuthenticatedPath = "/api/v1/bff/antiforgery";

    [Fact]
    public async Task Request_After15MinutesIdle_Returns401AndDeletesTheRow()
    {
        var clock = new TestClock();
        await using var factory = new ApiFactory(postgres.ConnectionString, clock: clock);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        string sub = $"idle-{Guid.NewGuid():N}";
        await browser.SignInAsync(factory.Zitadel, sub);

        clock.Advance(TimeSpan.FromMinutes(15));
        HttpResponseMessage response = await browser.GetPathAsync(AuthenticatedPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(await SessionExistsAsync(factory, sub));
    }

    [Fact]
    public async Task Request_ConcurrentWithTheSameExpiredCookie_AllReturn401()
    {
        // Every request finds the expired row before any of them deletes it (the interceptor holds each read), so
        // each one deletes a row another may already have deleted. None may fail for that.
        var clock = new TestClock();
        var holdSessionReads = new HoldSessionReads();
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            clock: clock,
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(holdSessionReads)));
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        string sub = $"concurrent-{Guid.NewGuid():N}";
        await browser.SignInAsync(factory.Zitadel, sub);
        clock.Advance(TimeSpan.FromMinutes(15));
        holdSessionReads.Enabled = true;

        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => browser.GetPathAsync(AuthenticatedPath)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        Assert.False(await SessionExistsAsync(factory, sub));
    }

    [Fact]
    public async Task Request_InsideTheIdleWindow_RenewsTheIdleLimit()
    {
        var clock = new TestClock();
        await using var factory = new ApiFactory(postgres.ConnectionString, clock: clock);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel);

        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetPathAsync(AuthenticatedPath)).StatusCode);
        clock.Advance(TimeSpan.FromMinutes(10));

        // 20 minutes after sign-in, but only 10 after the last request.
        Assert.Equal(HttpStatusCode.OK, (await browser.GetPathAsync(AuthenticatedPath)).StatusCode);
    }

    [Fact]
    public async Task Request_After12HoursOfActivity_Returns401AndDeletesTheRow()
    {
        var clock = new TestClock();
        await using var factory = new ApiFactory(postgres.ConnectionString, clock: clock);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        string sub = $"absolute-{Guid.NewGuid():N}";
        await browser.SignInAsync(factory.Zitadel, sub);

        // A request every 14 minutes keeps the idle limit moving for just under 12 hours.
        TimeSpan elapsed = TimeSpan.Zero;
        while (elapsed + TimeSpan.FromMinutes(14) < TimeSpan.FromHours(12))
        {
            clock.Advance(TimeSpan.FromMinutes(14));
            elapsed += TimeSpan.FromMinutes(14);
            Assert.Equal(HttpStatusCode.OK, (await browser.GetPathAsync(AuthenticatedPath)).StatusCode);
        }

        clock.Advance(TimeSpan.FromHours(12) - elapsed);
        HttpResponseMessage response = await browser.GetPathAsync(AuthenticatedPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(await SessionExistsAsync(factory, sub));
    }

    [Fact]
    public async Task DeleteExpiredAsync_AbandonedSession_DeletesOnlyExpiredRows()
    {
        // Its own database: the sweep, on a clock 16 minutes ahead, would delete every session other tests running in
        // parallel just started on the shared one.
        var clock = new TestClock();
        await using var factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync(), clock: clock);
        using HttpClient abandoned = factory.CreateBrowser(new CookieContainer());
        string abandonedSub = $"abandoned-{Guid.NewGuid():N}";
        await abandoned.SignInAsync(factory.Zitadel, abandonedSub);
        clock.Advance(TimeSpan.FromMinutes(16));
        using HttpClient fresh = factory.CreateBrowser(new CookieContainer());
        string freshSub = $"fresh-{Guid.NewGuid():N}";
        await fresh.SignInAsync(factory.Zitadel, freshSub);

        await factory.Services.GetRequiredService<ExpiredSessionCleanup>().DeleteExpiredAsync(TestContext.Current.CancellationToken);

        Assert.False(await SessionExistsAsync(factory, abandonedSub));
        Assert.True(await SessionExistsAsync(factory, freshSub));
    }

    [Fact]
    public async Task DeleteExpiredAsyncTest_RunningBetweenAnotherTestsSignInAndRequest_LeavesThatSessionLive()
    {
        // The interleaving that once failed the fallback probe in a full run: a test signs in on the shared database,
        // then the sweep test above (its clock 16 minutes ahead) runs before that test's next request.
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, $"bystander-{Guid.NewGuid():N}");

        await DeleteExpiredAsync_AbandonedSession_DeletesOnlyExpiredRows();

        Assert.Equal(HttpStatusCode.OK, (await browser.GetPathAsync(AuthenticatedPath)).StatusCode);
    }

    /// <summary>Looks in the factory's own database.</summary>
    private static async Task<bool> SessionExistsAsync(ApiFactory factory, string sub)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Session>()
            .AnyAsync(row => row.LoginSub == sub, TestContext.Current.CancellationToken);
    }

    /// <summary>Once enabled, holds each read of the Sessions table for a moment, so concurrent requests all read before any of them writes.</summary>
    private sealed class HoldSessionReads : DbCommandInterceptor
    {
        public bool Enabled { get; set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            bool isSessionRead = command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains("FROM \"Sessions\"", StringComparison.Ordinal);
            if (Enabled && isSessionRead)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            }

            return result;
        }
    }
}
