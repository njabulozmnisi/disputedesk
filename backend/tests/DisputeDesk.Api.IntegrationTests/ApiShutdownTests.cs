using System.Diagnostics;
using System.Net;
using System.Text;
using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests;

/// <summary>
/// 7.13 AC6 (NFR-RES-5): the Api finishes a request in flight when it stops. The Api runs on Kestrel
/// (<c>WebApplicationFactory.UseKestrel</c>), not the in-memory test server, so the host's graceful shutdown applies
/// (Microsoft Learn, .NET Generic Host: host shutdown; <c>HostOptions.ShutdownTimeout</c> defaults to 30 seconds).
/// </summary>
public class ApiShutdownTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(30);

    // Below the resilience handler's 2-second attempt timeout (AD-21), so the held read is answered, not retried.
    private static readonly TimeSpan TransactionReadHold = TimeSpan.FromSeconds(1.5);

    [Fact]
    public async Task Raise_InFlightWhenTheApiStops_Answers201AndCommitsItsRowsWithinTheShutdownTimeout()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync();
        var clock = new TestClock(stub.Now);

        // A seeded linked customer signs in through an in-memory instance on https://localhost; the cookies it gets are
        // sent to the Kestrel instance, which shares its database and so its sessions and key ring.
        await using var signIn = new ApiFactory(connectionString, clock: clock);
        await signIn.LinkAsync("login-a", TransactionsApi.CustomerA);
        var cookies = new CookieContainer();
        using HttpClient browser = signIn.CreateBrowser(cookies);
        await browser.SignInAsync(signIn.Zitadel, "login-a");
        string antiforgeryToken = await browser.AntiforgeryTokenAsync();

        string transaction = await TransactionJsonAsync();
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using FakeCoreBankingServer slowCoreBanking = await FakeCoreBankingServer.StartAsync(async context =>
        {
            readStarted.TrySetResult();
            await Task.Delay(TransactionReadHold, context.RequestAborted);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(transaction, context.RequestAborted);
        });
        await using var api = new ApiFactory(
            connectionString,
            settings: new Dictionary<string, string?> { ["CoreBanking:BaseUrl"] = slowCoreBanking.BaseUrl.ToString() },
            clock: clock);
        api.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        api.StartServer();
        string address = api.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();

        using var http = new HttpClient { BaseAddress = new Uri(address) };
        using var raise = new HttpRequestMessage(HttpMethod.Post, new Uri(DisputesApi.RaisePath, UriKind.Relative))
        {
            Content = new StringContent(DisputesApi.Body(), Encoding.UTF8, "application/json"),
        };
        raise.Headers.Add("Cookie", cookies.GetCookieHeader(ApiFactory.BaseAddress));
        raise.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        raise.Headers.TryAddWithoutValidation("Idempotency-Key", DisputesApi.NewKey());
        Task<HttpResponseMessage> raising = http.SendAsync(raise, TestContext.Current.CancellationToken);
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        var stopping = Stopwatch.StartNew();
        await api.DisposeAsync();
        stopping.Stop();

        HttpResponseMessage response = await raising;
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(RowCounts.One, await signIn.CountRowsAsync());
        Assert.True(stopping.Elapsed < ShutdownTimeout, $"Stopping took {stopping.Elapsed}.");
    }

    /// <summary>Customer A's disputable transaction, as the real stub answers its read.</summary>
    private async Task<string> TransactionJsonAsync()
    {
        using var http = new HttpClient { BaseAddress = stub.BaseUrl };
        using var read = new HttpRequestMessage(HttpMethod.Get, new Uri("transaction", UriKind.Relative));
        read.Headers.Add("X-Customer-Id", TransactionsApi.CustomerA);
        read.Headers.Add("X-Account-Ref", DisputesApi.AccountA);
        read.Headers.Add("X-Transaction-Id", DisputesApi.Disputable);
        using HttpResponseMessage response = await http.SendAsync(read, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }
}
