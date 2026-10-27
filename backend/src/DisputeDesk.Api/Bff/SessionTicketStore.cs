using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Infrastructure.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Api.Bff;

/// <summary>
/// Keeps the cookie handler's tickets in the <c>Sessions</c> table, so the cookie carries only a random key and any
/// Api instance can serve the session (AD-7). It sits in the Api, not Infrastructure, because <see cref="ITicketStore"/>
/// belongs to the ASP.NET Core shared framework, which the Worker's runtime image does not carry.
/// </summary>
public sealed class SessionTicketStore(IServiceScopeFactory scopes, IDataProtectionProvider dataProtection, TimeProvider time) : ITicketStore
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan AbsoluteTimeout = TimeSpan.FromHours(12);

    public const string TicketProtectorPurpose = "DisputeDesk.Sessions.Ticket";

    private readonly IDataProtector _protector = dataProtection.CreateProtector(TicketProtectorPurpose);

    /// <summary>Starts a session under a new 256-bit random key and returns the key, which only the cookie holds.</summary>
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        DateTimeOffset now = time.GetUtcNow();
        string key = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<Session>().Add(new Session
        {
            Id = Guid.CreateVersion7(now),
            KeyHash = Hash(key),
            LoginSub = ticket.Principal.FindFirst("sub")?.Value ?? throw new InvalidOperationException("The signed-in principal has no sub claim."),
            Ticket = Protect(ticket),
            ExpiresAtUtc = now + IdleTimeout,
            AbsoluteExpiresAtUtc = now + AbsoluteTimeout,
        });
        await db.SaveChangesAsync();
        return key;
    }

    /// <summary>Returns the session's ticket, or null when there is none. An expired session is deleted.</summary>
    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        byte[] keyHash = Hash(key);

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Session? session = await db.Set<Session>().AsNoTracking().SingleOrDefaultAsync(row => row.KeyHash == keyHash);
        if (session is null)
        {
            return null;
        }

        DateTimeOffset now = time.GetUtcNow();
        if (session.ExpiresAtUtc <= now || session.AbsoluteExpiresAtUtc <= now)
        {
            // One DELETE statement: requests racing on the same expired cookie each delete zero or one row, never fail.
            await db.Set<Session>().Where(row => row.KeyHash == keyHash).ExecuteDeleteAsync();
            return null;
        }

        return TicketSerializer.Default.Deserialize(_protector.Unprotect(session.Ticket));
    }

    /// <summary>Moves the idle limit forward. The absolute limit never moves.</summary>
    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        DateTimeOffset now = time.GetUtcNow();
        byte[] keyHash = Hash(key);
        byte[] protectedTicket = Protect(ticket);

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<Session>()
            .Where(row => row.KeyHash == keyHash)
            .ExecuteUpdateAsync(update => update
                .SetProperty(row => row.Ticket, protectedTicket)
                .SetProperty(row => row.ExpiresAtUtc, now + IdleTimeout));
    }

    public async Task RemoveAsync(string key)
    {
        byte[] keyHash = Hash(key);

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<Session>().Where(row => row.KeyHash == keyHash).ExecuteDeleteAsync();
    }

    private static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    private byte[] Protect(AuthenticationTicket ticket) => _protector.Protect(TicketSerializer.Default.Serialize(ticket));
}
