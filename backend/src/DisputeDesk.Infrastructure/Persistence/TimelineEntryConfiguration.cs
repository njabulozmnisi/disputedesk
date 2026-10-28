using DisputeDesk.Domain.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>The <c>TimelineEntries</c> table (AD-22); its relationship to <c>Disputes</c> is in <see cref="DisputeConfiguration"/>.</summary>
public sealed class TimelineEntryConfiguration : IEntityTypeConfiguration<TimelineEntry>
{
    public void Configure(EntityTypeBuilder<TimelineEntry> builder)
    {
        builder.ToTable("TimelineEntries");
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.FromStatus).HasConversion<string>();
        builder.Property(entry => entry.ToStatus).HasConversion<string>();
        builder.Property(entry => entry.Action).HasConversion<string>();
        builder.Property(entry => entry.ActorType).HasConversion<string>();
    }
}
