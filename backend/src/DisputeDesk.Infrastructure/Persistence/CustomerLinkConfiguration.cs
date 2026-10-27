using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputeDesk.Infrastructure.Persistence;

public sealed class CustomerLinkConfiguration : IEntityTypeConfiguration<CustomerLink>
{
    public void Configure(EntityTypeBuilder<CustomerLink> builder)
    {
        builder.ToTable("CustomerLinks");
        builder.Property(link => link.Id).ValueGeneratedNever();
        builder.HasIndex(link => link.LoginSub).IsUnique().HasDatabaseName("UQ_CustomerLinks_LoginSub");
        builder.HasIndex(link => link.CustomerId).IsUnique().HasDatabaseName("UQ_CustomerLinks_CustomerId");
    }
}
