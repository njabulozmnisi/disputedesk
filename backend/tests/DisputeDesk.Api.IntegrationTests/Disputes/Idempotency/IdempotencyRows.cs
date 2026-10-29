using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Disputes.RaiseDispute;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes.Idempotency;

/// <summary>Reads and ages <c>IdempotencyKeys</c> rows directly, as time passing would.</summary>
public static class IdempotencyRows
{
    public static async Task<List<IdempotencyKey>> KeyRowsAsync(this ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<IdempotencyKey>().AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Sets when every key's lease was taken, so a key looks as old as the test needs.</summary>
    public static async Task SetLeaseTakenAtAsync(this ApiFactory factory, DateTimeOffset createdAtUtc)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<IdempotencyKey>()
            .ExecuteUpdateAsync(setters => setters.SetProperty(key => key.CreatedAtUtc, createdAtUtc), TestContext.Current.CancellationToken);
    }

    public static async Task SetExpiresAtAsync(this ApiFactory factory, DateTimeOffset expiresAtUtc)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<IdempotencyKey>()
            .ExecuteUpdateAsync(setters => setters.SetProperty(key => key.ExpiresAtUtc, expiresAtUtc), TestContext.Current.CancellationToken);
    }

    public static Task<byte[]> BodyBytesAsync(this HttpResponseMessage response) =>
        response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

    /// <summary>Asserts <paramref name="replay"/> is <paramref name="original"/> again: status, media type, <c>Location</c> and body bytes.</summary>
    public static async Task AssertSameResponseAsync(HttpResponseMessage original, HttpResponseMessage replay)
    {
        Assert.NotEmpty(await original.BodyBytesAsync());
        Assert.Equal(original.StatusCode, replay.StatusCode);
        Assert.Equal(original.Content.Headers.ContentType, replay.Content.Headers.ContentType);
        Assert.Equal(original.Headers.Location, replay.Headers.Location);
        Assert.Equal(await original.BodyBytesAsync(), await replay.BodyBytesAsync());
    }
}
