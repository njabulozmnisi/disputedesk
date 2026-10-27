using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>A misbehaving core-banking stub on a loopback port: every request gets the given answer, and is counted.</summary>
public sealed class FakeCoreBankingServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private int _requestCount;

    private FakeCoreBankingServer(RequestDelegate answer)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        _app = builder.Build();
        _app.Run(context =>
        {
            Interlocked.Increment(ref _requestCount);
            return answer(context);
        });
    }

    public int RequestCount => Volatile.Read(ref _requestCount);

    public Uri BaseUrl => new(_app.Urls.First());

    /// <summary>Answers every request with 500.</summary>
    public static Task<FakeCoreBankingServer> FailingAsync() => StartAsync(context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return Task.CompletedTask;
    });

    /// <summary>Answers every request with 200 and a body that is not valid JSON.</summary>
    public static Task<FakeCoreBankingServer> MalformedAsync() => StartAsync(context =>
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync("[{\"accountRef\":", context.RequestAborted);
    });

    /// <summary>
    /// Sends 200 and the headers at once, then holds the given (valid) body back for 8 seconds, longer than the client's
    /// 5-second deadline.
    /// </summary>
    public static Task<FakeCoreBankingServer> StalledBodyAsync(string body) => StartAsync(async context =>
    {
        context.Response.ContentType = "application/json";
        try
        {
            await context.Response.StartAsync(context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await Task.Delay(TimeSpan.FromSeconds(8), context.RequestAborted);
            await context.Response.WriteAsync(body, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // The client gave up first, which is the point.
        }
    });

    /// <summary>Holds every request for 30 seconds before answering.</summary>
    public static Task<FakeCoreBankingServer> SlowAsync() => StartAsync(async context =>
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // The client gave up first, which is the point.
        }
    });

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    public static async Task<FakeCoreBankingServer> StartAsync(RequestDelegate answer)
    {
        var server = new FakeCoreBankingServer(answer);
        await server._app.StartAsync(TestContext.Current.CancellationToken);
        return server;
    }
}
