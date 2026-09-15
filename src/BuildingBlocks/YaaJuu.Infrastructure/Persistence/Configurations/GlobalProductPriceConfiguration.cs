using YaaJuu.Modules.Catalog.Domain;
using YaaJuu.Modules.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace YaaJuu.Infrastructure.Persistence.Configurations;

public sealed class GlobalProductPriceConfiguration : IEntityTypeConfiguration<GlobalProductPrice>
{
    public void Configure(EntityTypeBuilder<GlobalProductPrice> builder)
    {
        builder.ToTable("global_product_prices", table =>
        {
            table.HasCheckConstraint("ck_global_product_prices_amount_positive", "amount > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.ValidFrom).IsRequired();

        builder.HasIndex(x => x.GlobalProductId)
            .IsUnique()
            .HasFilter("valid_to IS NULL")
            .HasDatabaseName("ix_global_product_prices_current");

        builder.HasIndex(x => new { x.GlobalProductId, x.ValidFrom })
            .HasDatabaseName("ix_global_product_prices_product_valid_from");

        builder.HasOne<GlobalProduct>()
            .WithMany()
            .HasForeignKey(x => x.GlobalProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ConfigureXminConcurrency();
    }
}
