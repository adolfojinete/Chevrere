using Chevrere.Modules.Catalog.Domain;
using Chevrere.Modules.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chevrere.Infrastructure.Persistence.Configurations;

public sealed class StoreProductConfiguration : IEntityTypeConfiguration<StoreProduct>
{
    public void Configure(EntityTypeBuilder<StoreProduct> builder)
    {
        builder.ToTable("store_products");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.StoreId, x.GlobalProductId });

        builder.HasIndex(x => new { x.StoreId, x.GlobalProductId }).IsUnique();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.GlobalProductId);
        builder.HasIndex(x => x.IsEnabled);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<GlobalProduct>()
            .WithMany()
            .HasForeignKey(x => x.GlobalProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ConfigureXminConcurrency();
    }
}
