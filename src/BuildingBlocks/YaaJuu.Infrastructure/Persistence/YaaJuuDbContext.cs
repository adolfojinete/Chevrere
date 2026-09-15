using YaaJuu.Infrastructure.Audit;
using YaaJuu.Infrastructure.Identity;
using YaaJuu.Modules.Catalog.Domain;
using YaaJuu.Modules.Consumer.Domain;
using YaaJuu.Modules.Inventory.Domain;
using YaaJuu.Modules.Orders.Domain;
using YaaJuu.Modules.Pricing.Domain;
using YaaJuu.Modules.Procurement.Domain;
using YaaJuu.Modules.Subscriptions.Domain;
using YaaJuu.Modules.Tenancy.Domain;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace YaaJuu.Infrastructure.Persistence;

public sealed class YaaJuuDbContext(
    DbContextOptions<YaaJuuDbContext> options,
    ICurrentUser currentUser,
    ITenantFilterBypass tenantFilterBypass)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Franchisee> Franchisees => Set<Franchisee>();

    public DbSet<Store> Stores => Set<Store>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<GlobalProduct> GlobalProducts => Set<GlobalProduct>();

    public DbSet<StoreProduct> StoreProducts => Set<StoreProduct>();

    public DbSet<GlobalProductPrice> GlobalProductPrices => Set<GlobalProductPrice>();

    public DbSet<StoreProductPrice> StoreProductPrices => Set<StoreProductPrice>();

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();

    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();

    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<CartItem> CartItems => Set<CartItem>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();

    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();

    public DbSet<GoodsReceiptItem> GoodsReceiptItems => Set<GoodsReceiptItem>();

    public DbSet<StoreServiceArea> StoreServiceAreas => Set<StoreServiceArea>();

    public DbSet<IdempotentOperation> IdempotentOperations => Set<IdempotentOperation>();

    public bool BypassTenantFilter => tenantFilterBypass.Enabled || currentUser.IsPlatformUser;

    public Guid FilterTenantId => currentUser.TenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasPostgresExtension("postgis");
        builder.ApplyConfigurationsFromAssembly(typeof(YaaJuuDbContext).Assembly);

        builder.Entity<Tenant>().HasQueryFilter(t => BypassTenantFilter || t.Id == FilterTenantId);
        builder.Entity<Franchisee>().HasQueryFilter(f => BypassTenantFilter || f.TenantId == FilterTenantId);
        builder.Entity<Store>().HasQueryFilter(s => BypassTenantFilter || s.TenantId == FilterTenantId);
        builder.Entity<Subscription>().HasQueryFilter(s => BypassTenantFilter || s.TenantId == FilterTenantId);
        builder.Entity<AuditEvent>().HasQueryFilter(a => BypassTenantFilter || a.TenantId == FilterTenantId);
        builder.Entity<StoreProduct>().HasQueryFilter(s => BypassTenantFilter || s.TenantId == FilterTenantId);
        builder.Entity<StoreProductPrice>().HasQueryFilter(s => BypassTenantFilter || s.TenantId == FilterTenantId);
        builder.Entity<InventoryItem>().HasQueryFilter(i => BypassTenantFilter || i.TenantId == FilterTenantId);
        builder.Entity<InventoryMovement>().HasQueryFilter(m => BypassTenantFilter || m.TenantId == FilterTenantId);
        builder.Entity<InventoryReservation>().HasQueryFilter(r => BypassTenantFilter || r.TenantId == FilterTenantId);
        builder.Entity<Cart>().HasQueryFilter(c => BypassTenantFilter || c.TenantId == FilterTenantId);
        builder.Entity<CartItem>().HasQueryFilter(i => BypassTenantFilter || i.TenantId == FilterTenantId);
        builder.Entity<Order>().HasQueryFilter(o => BypassTenantFilter || o.TenantId == FilterTenantId);
        builder.Entity<OrderItem>().HasQueryFilter(i => BypassTenantFilter || i.TenantId == FilterTenantId);
        builder.Entity<Supplier>().HasQueryFilter(s => BypassTenantFilter || s.TenantId == FilterTenantId);
        builder.Entity<PurchaseOrder>().HasQueryFilter(o => BypassTenantFilter || o.TenantId == FilterTenantId);
        builder.Entity<PurchaseOrderItem>().HasQueryFilter(i => BypassTenantFilter || i.TenantId == FilterTenantId);
        builder.Entity<GoodsReceipt>().HasQueryFilter(r => BypassTenantFilter || r.TenantId == FilterTenantId);
        builder.Entity<GoodsReceiptItem>().HasQueryFilter(i => BypassTenantFilter || i.TenantId == FilterTenantId);
        builder.Entity<StoreServiceArea>().HasQueryFilter(a => BypassTenantFilter || a.TenantId == FilterTenantId);

        // Document numbers are drawn outside the transaction so retries never reuse a number.
        builder.HasSequence<long>("procurement_purchase_order_number_seq").StartsAt(1).IncrementsBy(1);
        builder.HasSequence<long>("procurement_goods_receipt_number_seq").StartsAt(1).IncrementsBy(1);
        builder.HasSequence<long>("orders_order_number_seq").StartsAt(1).IncrementsBy(1);

        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<ApplicationRole>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
    }
}
