using YaaJuu.Modules.Orders.Domain;
using YaaJuu.Modules.Payments.Domain;
using YaaJuu.Modules.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace YaaJuu.Infrastructure.Persistence.Configurations;

public sealed class PaymentMerchantConfigurationEntityConfiguration
    : IEntityTypeConfiguration<PaymentMerchantConfiguration>
{
    public void Configure(EntityTypeBuilder<PaymentMerchantConfiguration> builder)
    {
        builder.ToTable("payment_merchant_configurations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Environment).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.PublicKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.EncryptedPrivateKey).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.EncryptedIntegritySecret).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.EncryptedEventsSecret).HasMaxLength(4000).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Provider, x.Environment, x.Version })
            .IsUnique()
            .HasDatabaseName("ux_payment_merchant_cfg_tenant_provider_env_version");

        builder.HasIndex(x => new { x.TenantId, x.Provider, x.Environment })
            .IsUnique()
            .HasFilter("is_enabled = TRUE")
            .HasDatabaseName("ux_payment_merchant_cfg_active");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ConfigureXminConcurrency();
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", table =>
        {
            table.HasCheckConstraint("ck_payments_amount_positive", "amount > 0");
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.TenantId });

        builder.Property(x => x.Provider).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.ReconciliationReason).HasConversion<string>().HasMaxLength(64).IsRequired();

        builder.HasIndex(x => x.OrderId)
            .IsUnique()
            .HasDatabaseName("ux_payments_order_id");

        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.CreatedAt })
            .HasDatabaseName("ix_payments_tenant_store_created_at");

        builder.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("ix_payments_status_created_at");

        builder.HasIndex(x => x.RequiresReconciliation)
            .HasFilter("requires_reconciliation = TRUE")
            .HasDatabaseName("ix_payments_requires_reconciliation");

        builder.HasMany(x => x.Attempts)
            .WithOne()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Metadata.FindNavigation(nameof(Payment.Attempts))!
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

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => new { x.OrderId, x.TenantId, x.StoreId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId, x.StoreId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.ConfigureXminConcurrency();
    }
}

public sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("payment_attempts", table =>
        {
            table.HasCheckConstraint("ck_payment_attempts_amount_positive", "amount > 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Environment).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.MerchantReference).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ProviderTransactionId).HasMaxLength(128);
        builder.Property(x => x.ProviderRawStatus).HasMaxLength(64);
        builder.Property(x => x.FailureCode).HasMaxLength(64);
        builder.Property(x => x.FailureMessage).HasMaxLength(500);
        builder.Property(x => x.CheckoutActionJson).HasMaxLength(8000);

        builder.HasIndex(x => x.MerchantReference)
            .IsUnique()
            .HasDatabaseName("ux_payment_attempts_merchant_reference");

        builder.HasIndex(x => new { x.Provider, x.Environment, x.ProviderTransactionId })
            .IsUnique()
            .HasFilter("provider_transaction_id IS NOT NULL")
            .HasDatabaseName("ux_payment_attempts_provider_tx");

        builder.HasIndex(x => x.PaymentId)
            .IsUnique()
            .HasFilter("status IN ('Created', 'Initiating', 'Pending', 'Unknown')")
            .HasDatabaseName("ux_payment_attempts_inflight");

        builder.HasIndex(x => new { x.Status, x.LastProviderCheckAt, x.CreatedAt })
            .HasDatabaseName("ix_payment_attempts_reconciliation");

        builder.HasOne<PaymentMerchantConfiguration>()
            .WithMany()
            .HasForeignKey(x => x.MerchantConfigurationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ConfigureXminConcurrency();
    }
}

public sealed class PaymentProviderEventConfiguration : IEntityTypeConfiguration<PaymentProviderEvent>
{
    public void Configure(EntityTypeBuilder<PaymentProviderEvent> builder)
    {
        builder.ToTable("payment_provider_events");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Environment).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProviderEventId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PayloadHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(500);

        builder.HasIndex(x => new { x.Provider, x.Environment, x.ProviderEventId })
            .IsUnique()
            .HasDatabaseName("ux_payment_provider_events_id");

        builder.HasOne<PaymentAttempt>()
            .WithMany()
            .HasForeignKey(x => x.PaymentAttemptId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne<PaymentMerchantConfiguration>()
            .WithMany()
            .HasForeignKey(x => x.MerchantConfigurationId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
