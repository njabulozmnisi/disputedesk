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
        builder.Property(dispute => dispute.Outcome).HasConversion<string>();

        // No concurrency token: every write to Disputes commits with a stored event, so UQ_StoredEvents_DisputeId_Version
        // already catches every concurrent change (AD-15, ad-009).

        // The pending stored events become StoredEvents rows at the commit (AD-14), never a navigation.
        builder.Ignore(dispute => dispute.PendingChanges);

        builder.HasIndex(dispute => new { dispute.AccountRef, dispute.TransactionId })
            .IsUnique()
            .HasDatabaseName(OneDisputePerTransaction);

        // The SLA sweep's claim (AD-18) reads only the disputes it can still escalate.
        builder.HasIndex(dispute => dispute.SlaDueAtUtc)
            .HasDatabaseName("IX_Disputes_SlaDueAtUtc")
            .HasFilter("\"Status\" IN ('Submitted', 'UnderReview')");
    }
}
