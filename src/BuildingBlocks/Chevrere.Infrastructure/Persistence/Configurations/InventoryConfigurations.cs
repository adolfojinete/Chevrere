using Chevrere.Modules.Catalog.Domain;
using Chevrere.Modules.Inventory.Domain;
using Chevrere.Modules.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chevrere.Infrastructure.Persistence.Configurations;

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

public sealed class IdempotentOperationConfiguration : IEntityTypeConfiguration<Chevrere.SharedKernel.Idempotency.IdempotentOperation>
{
    public void Configure(EntityTypeBuilder<Chevrere.SharedKernel.Idempotency.IdempotentOperation> builder)
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
