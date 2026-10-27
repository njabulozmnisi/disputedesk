using DisputeDesk.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>The PostgreSQL unit of work. Feature stories add their entity configurations to this assembly.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext, IDataProtectionKeyContext
{
    /// <summary>The Data Protection key ring, shared by every Api instance (AD-7).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
