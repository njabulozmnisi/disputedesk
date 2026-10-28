using DisputeDesk.Domain.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>The <c>Disputes</c> table (AD-22). The ownership filter is in <see cref="AppDbContext"/>, which holds its parameter.</summary>
public sealed class DisputeConfiguration : IEntityTypeConfiguration<Dispute>
{
    /// <summary>One dispute per transaction, ever (AD-15); <see cref="UniqueViolations"/> maps a loss on it to 409.</summary>
    public const string OneDisputePerTransaction = "UQ_Disputes_AccountRef_TransactionId";

    public void Configure(EntityTypeBuilder<Dispute> builder)
    {
        builder.ToTable("Disputes");
        builder.Property(dispute => dispute.Id).ValueGeneratedNever();

        // Money is numeric(19,2) plus an ISO 4217 code (AD-4).
        builder.Property(dispute => dispute.TransactionAmount).HasPrecision(19, 2);
        builder.Property(dispute => dispute.ExpectedAmount).HasPrecision(19, 2);
        builder.Property(dispute => dispute.DisputedAmount).HasPrecision(19, 2);
        builder.Property(dispute => dispute.Currency).HasMaxLength(3);

        builder.Property(dispute => dispute.Reason).HasConversion<string>();
        builder.Property(dispute => dispute.Status).HasConversion<string>();

        // Postgres's xmin system column as the concurrency token (Npgsql: a uint row version maps to xmin), so no
        // migration adds a column. A shadow property keeps it out of the Domain (AD-15).
        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.HasIndex(dispute => new { dispute.AccountRef, dispute.TransactionId })
            .IsUnique()
            .HasDatabaseName(OneDisputePerTransaction);

        // The SLA sweep's claim (AD-18) reads only the disputes it can still escalate.
        builder.HasIndex(dispute => dispute.SlaDueAtUtc)
            .HasDatabaseName("IX_Disputes_SlaDueAtUtc")
            .HasFilter("\"Status\" IN ('Submitted', 'UnderReview')");

        // Nothing is deleted from Disputes in v1 (AD-22), so a delete must never take the timeline with it.
        builder.HasMany(dispute => dispute.Timeline)
            .WithOne(entry => entry.Dispute)
            .HasForeignKey(entry => entry.DisputeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
