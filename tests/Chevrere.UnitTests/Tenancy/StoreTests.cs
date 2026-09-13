using Chevrere.Modules.Tenancy.Domain;
using Chevrere.Modules.Tenancy.Domain.ValueObjects;
using Chevrere.SharedKernel.Domain;

namespace Chevrere.UnitTests.Tenancy;

public sealed class StoreTests
{
    [Fact]
    public void Store_belongs_to_same_tenant_as_franchisee()
    {
        var tenant = TestEntities.Tenant();
        var franchisee = TestEntities.Franchisee(tenant);
        var store = TestEntities.Store(tenant, franchisee);

        Assert.Equal(tenant.Id, store.TenantId);
        Assert.Equal(franchisee.Id, store.FranchiseeId);
        Assert.True(store.BelongsTo(tenant.Id, franchisee.Id));
    }

    [Fact]
    public void Store_cannot_join_franchisee_from_another_tenant()
    {
        var tenantA = TestEntities.Tenant("TENANT-A");
        var tenantB = TestEntities.Tenant("TENANT-B");
        var franchiseeB = TestEntities.Franchisee(tenantB, "FR-B");

        var ex = Assert.Throws<DomainException>(() =>
            Store.Create(
                tenantA,
                franchiseeB,
                TenantCode.Create("DS-001"),
                "Store",
                "Calle 1",
                null,
                FixedClock.Now));

        Assert.Equal("store.tenant_mismatch", ex.Code);
    }
}
