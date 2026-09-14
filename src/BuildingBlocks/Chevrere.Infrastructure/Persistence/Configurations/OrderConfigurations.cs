using Chevrere.Infrastructure.Identity;
using Chevrere.Modules.Catalog.Domain;
using Chevrere.Modules.Orders.Domain;
using Chevrere.Modules.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chevrere.Infrastructure.Persistence.Configurations;

public sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("carts");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.ConsumerUserId, x.TenantId, x.StoreId })
            .HasName("ak_carts_id_consumer_tenant_store");

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasIndex(x => x.ConsumerUserId)
            .IsUnique()
            .HasFilter("status = 'Active'")
            .HasDatabaseName("ix_carts_consumer_active");
        builder.HasIndex(x => new { x.TenantId, x.StoreId });

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Cart.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.ConsumerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ConfigureXminConcurrency();
    }
}

public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("cart_items", table =>
        {
            table.HasCheckConstraint("ck_cart_items_quantity_positive", "quantity > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).IsRequired();

        builder.HasIndex(x => new { x.CartId, x.GlobalProductId })
            .IsUnique()
            .HasDatabaseName("ix_cart_items_cart_product");
        builder.HasIndex(x => x.TenantId);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
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
    }
}

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", table =>
        {
            table.HasCheckConstraint("ck_orders_subtotal_positive", "subtotal_amount > 0");
            table.HasCheckConstraint("ck_orders_total_positive", "total_amount > 0");
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.TenantId });

        builder.Property(x => x.Number).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.SubtotalAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.CancelReason).HasMaxLength(1000);

        builder.HasIndex(x => x.SourceCartId)
            .IsUnique()
            .HasDatabaseName("ix_orders_source_cart_id");
        builder.HasIndex(x => new { x.TenantId, x.Number })
            .IsUnique()
            .HasDatabaseName("ix_orders_tenant_number");
        builder.HasIndex(x => new { x.Status, x.ExpiresAt })
            .HasDatabaseName("ix_orders_status_expires_at");
        builder.HasIndex(x => new { x.ConsumerUserId, x.CreatedAt })
            .HasDatabaseName("ix_orders_consumer_created_at");
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.CreatedAt })
            .HasDatabaseName("ix_orders_tenant_store_created_at");

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => new { x.OrderId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Order.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Cart>()
            .WithMany()
            .HasForeignKey(x => new { x.SourceCartId, x.ConsumerUserId, x.TenantId, x.StoreId })
            .HasPrincipalKey(x => new { x.Id, x.ConsumerUserId, x.TenantId, x.StoreId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.ConsumerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ConfigureXminConcurrency();
    }
}

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items", table =>
        {
            table.HasCheckConstraint("ck_order_items_quantity_positive", "quantity > 0");
            table.HasCheckConstraint("ck_order_items_unit_price_positive", "unit_price_amount > 0");
            table.HasCheckConstraint("ck_order_items_line_total_positive", "line_total_amount > 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Brand).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Presentation).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Quantity).IsRequired();
        builder.Property(x => x.UnitPriceAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.UnitPriceCurrency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.LineTotalAmount).HasPrecision(18, 2).IsRequired();

        builder.HasIndex(x => new { x.OrderId, x.GlobalProductId })
            .IsUnique()
            .HasDatabaseName("ix_order_items_order_product");
        builder.HasIndex(x => x.TenantId);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<GlobalProduct>()
            .WithMany()
            .HasForeignKey(x => x.GlobalProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StoreProduct>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StoreId, x.GlobalProductId })
            .HasPrincipalKey(x => new { x.TenantId, x.StoreId, x.GlobalProductId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
