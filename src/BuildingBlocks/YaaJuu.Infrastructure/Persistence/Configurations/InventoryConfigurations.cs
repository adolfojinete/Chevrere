using YaaJuu.Modules.Catalog.Domain;
using YaaJuu.Modules.Inventory.Domain;
using YaaJuu.Modules.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace YaaJuu.Infrastructure.Persistence.Configurations;

public sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("inventory_items", table =>
        {
            table.HasCheckConstraint("ck_inventory_items_on_hand_non_negative", "on_hand >= 0");
            table.HasCheckConstraint("ck_inventory_items_reserved_non_negative", "reserved >= 0");
            table.HasCheckConstraint("ck_inventory_items_reserved_lte_on_hand", "reserved <= on_hand");
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.TenantId });
        builder.Ignore(x => x.Available);

        builder.Property(x => x.OnHand).IsRequired();
        builder.Property(x => x.Reserved).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.GlobalProductId })
            .IsUnique()
            .HasDatabaseName("ix_inventory_items_tenant_store_product");

        builder.HasAlternateKey(x => new { x.Id, x.TenantId, x.StoreId, x.GlobalProductId })
            .HasName("ak_inventory_items_id_tenant_store_product");

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.StoreId);

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

public sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ToTable("inventory_movements");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.ReferenceType).HasMaxLength(64);
        builder.Property(x => x.CorrelationId).HasMaxLength(64).IsRequired();

        builder.HasIndex(x => new { x.InventoryItemId, x.OccurredAt })
            .HasDatabaseName("ix_inventory_movements_item_occurred_at");
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.ReferenceId);

        // A referenced document line may only ever post one movement of a given type. Guarantees a
        // goods receipt cannot be applied to stock twice, even under concurrent retries.
        builder.HasIndex(x => new { x.Type, x.ReferenceType, x.ReferenceId })
            .IsUnique()
            .HasFilter("reference_id IS NOT NULL")
            .HasDatabaseName("ix_inventory_movements_reference_unique");

        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(x => new { x.InventoryItemId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable("inventory_reservations", table =>
        {
            table.HasCheckConstraint("ck_inventory_reservations_quantity_positive", "quantity > 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ReferenceType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Quantity).IsRequired();

        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId })
            .IsUnique()
            .HasDatabaseName("ix_inventory_reservations_reference");
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.Status })
            .HasDatabaseName("ix_inventory_reservations_tenant_store_status");
        builder.HasIndex(x => x.InventoryItemId);

        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(x => new { x.InventoryItemId, x.TenantId, x.StoreId, x.GlobalProductId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId, x.StoreId, x.GlobalProductId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.ConfigureXminConcurrency();
    }
}

public sealed class IdempotentOperationConfiguration : IEntityTypeConfiguration<YaaJuu.SharedKernel.Idempotency.IdempotentOperation>
{
    public void Configure(EntityTypeBuilder<YaaJuu.SharedKernel.Idempotency.IdempotentOperation> builder)
    {
        builder.ToTable("idempotent_operations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Operation).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Operation, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ix_idempotent_operations_tenant_operation_key");
    }
}
