using DisputeDesk.Application.Linking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>The <c>LinkingChallenges</c> table (AD-12, AD-22).</summary>
public sealed class LinkingChallengeConfiguration : IEntityTypeConfiguration<LinkingChallenge>
{
    /// <summary>One current challenge per login: neither superseded nor consumed (AD-15).</summary>
    public const string OneCurrentChallengePerLogin = "UQ_LinkingChallenges_LoginSub";

    public void Configure(EntityTypeBuilder<LinkingChallenge> builder)
    {
        builder.ToTable("LinkingChallenges");
        builder.Property(challenge => challenge.Id).ValueGeneratedNever();

        // A filtered unique index (EF Core, Indexes: index filter), so superseded and consumed challenges stay as records.
        builder.HasIndex(challenge => challenge.LoginSub)
            .IsUnique()
            .HasFilter("\"SupersededAtUtc\" IS NULL AND \"ConsumedAtUtc\" IS NULL")
            .HasDatabaseName(OneCurrentChallengePerLogin);
    }
}
