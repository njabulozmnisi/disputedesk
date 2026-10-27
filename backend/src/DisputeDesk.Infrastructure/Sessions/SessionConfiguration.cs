using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputeDesk.Infrastructure.Sessions;

public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("Sessions");
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.HasIndex(session => session.KeyHash).IsUnique().HasDatabaseName("UQ_Sessions_KeyHash");
    }
}
