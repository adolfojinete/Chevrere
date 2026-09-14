using Chevrere.Modules.Catalog.Domain;
using Chevrere.Modules.Procurement.Domain;
using Chevrere.Modules.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chevrere.Infrastructure.Persistence.Configurations;

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.TenantId });

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TaxIdentification).HasMaxLength(50);
        builder.Property(x => x.ContactName).HasMaxLength(160);
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.Phone).HasMaxLength(40);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.Notes).HasMaxLength(1000);

        builder.HasIndex(x => new { x.TenantId, x.Code })
            .IsUnique()
            .HasDatabaseName("ix_suppliers_tenant_code");
        builder.HasIndex(x => x.IsActive);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ConfigureXminConcurrency();
    }
}

public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.TenantId });
        builder.Ignore(x => x.IsDraft);

        builder.Property(x => x.Number).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.CancelReason).HasMaxLength(1000);

        builder.HasIndex(x => new { x.TenantId, x.Number })
            .IsUnique()
            .HasDatabaseName("ix_purchase_orders_tenant_number");
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.Status })
            .HasDatabaseName("ix_purchase_orders_tenant_store_status");
        builder.HasIndex(x => x.SupplierId);

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => new { x.PurchaseOrderId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(PurchaseOrder.Items))!
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

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(x => new { x.SupplierId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.ConfigureXminConcurrency();
    }
}

public sealed class PurchaseOrderItemConfiguration : IEntityTypeConfiguration<PurchaseOrderItem>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderItem> builder)
    {
        builder.ToTable("purchase_order_items", table =>
        {
            table.HasCheckConstraint("ck_purchase_order_items_ordered_positive", "ordered_quantity > 0");
            table.HasCheckConstraint("ck_purchase_order_items_received_non_negative", "received_quantity >= 0");
            table.HasCheckConstraint(
                "ck_purchase_order_items_received_lte_ordered",
                "received_quantity <= ordered_quantity");
            table.HasCheckConstraint("ck_purchase_order_items_unit_cost_positive", "unit_cost_amount > 0");
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.TenantId });
        builder.Ignore(x => x.RemainingQuantity);
        builder.Ignore(x => x.IsFullyReceived);

        builder.Property(x => x.OrderedQuantity).IsRequired();
        builder.Property(x => x.ReceivedQuantity).IsRequired();
        builder.Property(x => x.UnitCostAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.UnitCostCurrency).HasMaxLength(3).IsRequired();

        builder.HasIndex(x => new { x.PurchaseOrderId, x.GlobalProductId })
            .IsUnique()
            .HasDatabaseName("ix_purchase_order_items_order_product");
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

        builder.ConfigureXminConcurrency();
    }
}

public sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.ToTable("goods_receipts");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.TenantId });

        builder.Property(x => x.ReceiptNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PurchaseOrderStatusAfter)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.ReceiptNumber })
            .IsUnique()
            .HasDatabaseName("ix_goods_receipts_tenant_number");
        builder.HasIndex(x => new { x.PurchaseOrderId, x.ReceivedAt })
            .HasDatabaseName("ix_goods_receipts_order_received_at");

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => new { x.GoodsReceiptId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(GoodsReceipt.Items))!
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

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(x => new { x.SupplierId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PurchaseOrder>()
            .WithMany()
            .HasForeignKey(x => new { x.PurchaseOrderId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GoodsReceiptItemConfiguration : IEntityTypeConfiguration<GoodsReceiptItem>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptItem> builder)
    {
        builder.ToTable("goods_receipt_items", table =>
        {
            table.HasCheckConstraint("ck_goods_receipt_items_received_positive", "received_quantity > 0");
            table.HasCheckConstraint("ck_goods_receipt_items_received_before_non_negative", "received_before >= 0");
            table.HasCheckConstraint("ck_goods_receipt_items_remaining_non_negative", "remaining_after >= 0");
            table.HasCheckConstraint(
                "ck_goods_receipt_items_received_chain",
                "received_after = received_before + received_quantity");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ReceivedQuantity).IsRequired();
        builder.Property(x => x.ReceivedBefore).IsRequired();
        builder.Property(x => x.ReceivedAfter).IsRequired();
        builder.Property(x => x.RemainingAfter).IsRequired();

        builder.HasIndex(x => new { x.GoodsReceiptId, x.PurchaseOrderItemId })
            .IsUnique()
            .HasDatabaseName("ix_goods_receipt_items_receipt_order_item");
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.InventoryMovementId);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PurchaseOrderItem>()
            .WithMany()
            .HasForeignKey(x => new { x.PurchaseOrderItemId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<GlobalProduct>()
            .WithMany()
            .HasForeignKey(x => x.GlobalProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
