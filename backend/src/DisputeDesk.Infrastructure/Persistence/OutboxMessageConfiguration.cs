using DisputeDesk.Domain.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>The <c>OutboxMessages</c> table (AD-17, AD-22).</summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Status).HasConversion<string>();

        builder.HasOne<Dispute>()
            .WithMany()
            .HasForeignKey(message => message.DisputeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
