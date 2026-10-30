using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputeDesk.Infrastructure.Persistence;

public sealed class CustomerLinkConfiguration : IEntityTypeConfiguration<CustomerLink>
{
    /// <summary>One link per login (AD-10, AD-15).</summary>
    public const string OneLinkPerLogin = "UQ_CustomerLinks_LoginSub";

    /// <summary>One link per customer (AD-10, AD-15).</summary>
    public const string OneLinkPerCustomer = "UQ_CustomerLinks_CustomerId";

    public void Configure(EntityTypeBuilder<CustomerLink> builder)
    {
        builder.ToTable("CustomerLinks");
        builder.Property(link => link.Id).ValueGeneratedNever();
        builder.HasIndex(link => link.LoginSub).IsUnique().HasDatabaseName(OneLinkPerLogin);
        builder.HasIndex(link => link.CustomerId).IsUnique().HasDatabaseName(OneLinkPerCustomer);
    }
}
