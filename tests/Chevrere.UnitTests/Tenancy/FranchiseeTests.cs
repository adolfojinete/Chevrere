using Chevrere.Modules.Tenancy.Domain;
using Chevrere.Modules.Tenancy.Domain.ValueObjects;
using Chevrere.SharedKernel.Domain;

namespace Chevrere.UnitTests.Tenancy;

public sealed class FranchiseeTests
{
    [Fact]
    public void Create_binds_franchisee_to_tenant()
    {
        var tenant = TestEntities.Tenant();
        var franchisee = TestEntities.Franchisee(tenant);

        Assert.Equal(tenant.Id, franchisee.TenantId);
        Assert.Equal(FranchiseeStatus.Pending, franchisee.Status);
        Assert.True(franchisee.BelongsTo(tenant.Id));
    }

    [Fact]
    public void Activate_from_pending_is_valid()
    {
        var franchisee = TestEntities.Franchisee();
        franchisee.Activate(FixedClock.Now);
        Assert.Equal(FranchiseeStatus.Active, franchisee.Status);
    }

    [Fact]
    public void Suspend_from_pending_is_rejected()
    {
        var franchisee = TestEntities.Franchisee();
        var ex = Assert.Throws<DomainException>(() => franchisee.Suspend(FixedClock.Now));
        Assert.Equal("franchisee.invalid_transition", ex.Code);
    }

    [Fact]
    public void Suspend_then_reactivate_restores_active()
    {
        var franchisee = TestEntities.Franchisee();
        franchisee.Activate(FixedClock.Now);
        franchisee.Suspend(FixedClock.Now.AddMinutes(1));
        franchisee.Reactivate(FixedClock.Now.AddMinutes(2));

        Assert.Equal(FranchiseeStatus.Active, franchisee.Status);
    }

    [Fact]
    public void Reactivate_from_active_is_rejected()
    {
        var franchisee = TestEntities.Franchisee();
        franchisee.Activate(FixedClock.Now);
        Assert.Throws<DomainException>(() => franchisee.Reactivate(FixedClock.Now));
    }
}
