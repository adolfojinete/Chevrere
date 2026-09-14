using Chevrere.Infrastructure.Audit;
using Chevrere.Infrastructure.Identity;
using Chevrere.Modules.Catalog.Domain;
using Chevrere.Modules.Pricing.Domain;
using Chevrere.Modules.Subscriptions.Domain;
using Chevrere.Modules.Tenancy.Domain;
using Chevrere.SharedKernel.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Chevrere.Infrastructure.Persistence;

public sealed class ChevrereDbContext(
    DbContextOptions<ChevrereDbContext> options,
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

    public bool BypassTenantFilter => tenantFilterBypass.Enabled || currentUser.IsPlatformUser;

    public Guid FilterTenantId => currentUser.TenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ChevrereDbContext).Assembly);

        builder.Entity<Tenant>().HasQueryFilter(t => BypassTenantFilter || t.Id == FilterTenantId);
        builder.Entity<Franchisee>().HasQueryFilter(f => BypassTenantFilter || f.TenantId == FilterTenantId);
        builder.Entity<Store>().HasQueryFilter(s => BypassTenantFilter || s.TenantId == FilterTenantId);
        builder.Entity<Subscription>().HasQueryFilter(s => BypassTenantFilter || s.TenantId == FilterTenantId);
        builder.Entity<AuditEvent>().HasQueryFilter(a => BypassTenantFilter || a.TenantId == FilterTenantId);
        builder.Entity<StoreProduct>().HasQueryFilter(s => BypassTenantFilter || s.TenantId == FilterTenantId);
        builder.Entity<StoreProductPrice>().HasQueryFilter(s => BypassTenantFilter || s.TenantId == FilterTenantId);

        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<ApplicationRole>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
    }
}
