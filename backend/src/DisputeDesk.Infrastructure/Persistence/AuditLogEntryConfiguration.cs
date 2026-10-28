using DisputeDesk.Application.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>
/// The <c>AuditLogEntries</c> table (AD-14a, AD-22). <c>DisputeId</c> has no foreign key, because linking entries
/// have no dispute. Append-only is enforced by <see cref="AppDbContext"/>'s commit.
/// </summary>
public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLogEntries");
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Action).HasConversion<string>();
        builder.Property(entry => entry.Result).HasConversion<string>();
        builder.Property(entry => entry.ActorType).HasConversion<string>();
    }
}
