using DisputeDesk.Application.Disputes.RaiseDispute;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>The <c>IdempotencyKeys</c> table (AD-16, AD-22).</summary>
public sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    /// <summary>Keys are scoped to the customer; the claim's <c>ON CONFLICT</c> targets this index.</summary>
    public const string OneKeyPerCustomer = "UQ_IdempotencyKeys_CustomerId_Key";

    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("IdempotencyKeys");
        builder.Property(key => key.Id).ValueGeneratedNever();
        builder.Property(key => key.Key).HasMaxLength(IdempotencyKey.KeyMaxLength);
        builder.Property(key => key.Status).HasConversion<string>();

        // EF Core adds the lease to the WHERE of every update it sends for this row, so the completion of a raise
        // fails, and its whole save rolls back, when another request has taken the key over (AD-16).
        builder.Property(key => key.LeaseId).IsConcurrencyToken();

        builder.HasIndex(key => new { key.CustomerId, key.Key })
            .IsUnique()
            .HasDatabaseName(OneKeyPerCustomer);
    }
}
