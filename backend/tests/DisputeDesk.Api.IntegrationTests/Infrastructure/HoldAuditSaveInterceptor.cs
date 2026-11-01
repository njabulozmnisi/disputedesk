using System.Data.Common;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// A client that disconnects while an audit entry is being saved (Microsoft Learn, EF Core interceptors). The first save
/// that adds an entry of <paramref name="action"/> is held in <c>SavingChangesAsync</c>, before any INSERT, until the
/// request making it is aborted (<see cref="HttpContext.RequestAborted"/> has fired); then the save goes on, and
/// <see cref="SendAndDisconnectAsync"/> reports whether it committed. Given <paramref name="readBeforeTheSave"/>, the
/// hold is instead on the read just before that save, before it runs: the <paramref name="readNumber"/>th command whose
/// SQL contains that text.
/// </summary>
public sealed class HoldAuditSaveInterceptor(AuditAction action, string? readBeforeTheSave = null, int readNumber = 1)
    : SaveChangesInterceptor, IDbCommandInterceptor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpContextAccessor _requests = new();
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _committed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DbContext? _held;
    private int _reads;

    /// <summary>Adds the interceptor to the Api's database context, and the accessor it reads the request through.</summary>
    public void AddTo(IServiceCollection services)
    {
        services.AddSingleton<IHttpContextAccessor>(_requests);
        services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(this));
    }

    /// <summary>
    /// Sends <paramref name="request"/>, disconnects once the audit save (or the read before it) is held, and returns true
    /// when that save then committed, false when it failed or was cancelled.
    /// </summary>
    public async Task<bool> SendAndDisconnectAsync(HttpClient client, HttpRequestMessage request)
    {
        using var disconnect = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<HttpResponseMessage> sending = client.SendAsync(request, disconnect.Token);
        await _reached.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        await disconnect.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        return await _committed.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        DbContext? context = eventData.Context;
        if (context is null || !AddsEntry(context) || Interlocked.CompareExchange(ref _held, context, null) is not null)
        {
            return result;
        }

        if (readBeforeTheSave is null)
        {
            await HoldUntilTheRequestIsAbortedAsync();
        }

        return result;
    }

    public async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (readBeforeTheSave is not null
            && command.CommandText.Contains(readBeforeTheSave, StringComparison.Ordinal)
            && Interlocked.Increment(ref _reads) == readNumber)
        {
            await HoldUntilTheRequestIsAbortedAsync();
        }

        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Finish(eventData.Context, committed: true);
        return ValueTask.FromResult(result);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Finish(eventData.Context, committed: false);
        return Task.CompletedTask;
    }

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        Finish(eventData.Context, committed: false);
        return Task.CompletedTask;
    }

    private async Task HoldUntilTheRequestIsAbortedAsync()
    {
        CancellationToken requestAborted = _requests.HttpContext?.RequestAborted
            ?? throw new InvalidOperationException("The held command is not part of a request.");
        var aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (requestAborted.Register(() => aborted.TrySetResult()))
        {
            _reached.TrySetResult();
            await aborted.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        }
    }

    private bool AddsEntry(DbContext context) =>
        context.ChangeTracker.Entries<AuditLogEntry>().Any(entry => entry.State == EntityState.Added && entry.Entity.Action == action);

    private void Finish(DbContext? context, bool committed)
    {
        if (context is not null && context == Volatile.Read(ref _held))
        {
            _committed.TrySetResult(committed);
        }
    }
}
