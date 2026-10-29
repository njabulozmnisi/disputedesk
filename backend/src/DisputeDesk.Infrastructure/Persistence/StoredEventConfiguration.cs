using DisputeDesk.Application.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>
/// The <c>StoredEvents</c> table (ad-009, AD-22). The ownership filter is in <see cref="AppDbContext"/>, which holds its
/// parameter; insert-only is enforced by its commit.
/// </summary>
public sealed class StoredEventConfiguration : IEntityTypeConfiguration<StoredEventRow>
{
    /// <summary>
    /// One stored event per version of a dispute: the one concurrency check (AD-15). <see cref="UniqueViolations"/> maps
    /// a loss on it to the version clash. It also serves reading a stream in order, so it is the table's only index.
    /// </summary>
    public const string OneEventPerVersion = "UQ_StoredEvents_DisputeId_Version";

    public void Configure(EntityTypeBuilder<StoredEventRow> builder)
    {
        builder.ToTable("StoredEvents");
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.ActorType).HasConversion<string>();

        // jsonb, because Data is read back as JSON and never re-sent byte for byte, unlike the outbox payload (AD-22).
        builder.Property(row => row.Data).HasColumnType("jsonb");

        builder.HasIndex(row => new { row.DisputeId, row.Version })
            .IsUnique()
            .HasDatabaseName(OneEventPerVersion);

        // Nothing is deleted from Disputes in v1 (AD-22), so a delete must never take the stream with it.
        builder.HasOne(row => row.Dispute)
            .WithMany()
            .HasForeignKey(row => row.DisputeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
