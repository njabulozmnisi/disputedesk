using DisputeDesk.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>The PostgreSQL unit of work. No entities yet; feature stories add their configurations.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
