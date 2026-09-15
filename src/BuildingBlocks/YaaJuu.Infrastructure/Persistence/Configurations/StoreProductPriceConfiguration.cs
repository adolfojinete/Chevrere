using YaaJuu.Modules.Catalog.Domain;
using YaaJuu.Modules.Pricing.Domain;
using YaaJuu.Modules.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace YaaJuu.Infrastructure.Persistence.Configurations;

public sealed class StoreProductPriceConfiguration : IEntityTypeConfiguration<StoreProductPrice>
{
    public void Configure(EntityTypeBuilder<StoreProductPrice> builder)
    {
        builder.ToTable("store_product_prices", table =>
        {
            table.HasCheckConstraint("ck_store_product_prices_amount_positive", "amount > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.ValidFrom).IsRequired();

        builder.HasIndex(x => new { x.StoreId, x.GlobalProductId })
            .IsUnique()
            .HasFilter("valid_to IS NULL")
            .HasDatabaseName("ix_store_product_prices_current");

        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_store_product_prices_tenant_id");

        builder.HasIndex(x => new { x.StoreId, x.GlobalProductId, x.ValidFrom })
            .HasDatabaseName("ix_store_product_prices_store_product_valid_from");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StoreProduct>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StoreId, x.GlobalProductId })
            .HasPrincipalKey(x => new { x.TenantId, x.StoreId, x.GlobalProductId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<GlobalProduct>()
            .WithMany()
            .HasForeignKey(x => x.GlobalProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ConfigureXminConcurrency();
    }
}
