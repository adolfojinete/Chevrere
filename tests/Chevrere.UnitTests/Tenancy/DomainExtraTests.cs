using Chevrere.Modules.Subscriptions.Domain;
using Chevrere.Modules.Tenancy.Domain;
using Chevrere.SharedKernel.Domain;

namespace Chevrere.UnitTests.Tenancy;

public sealed class DomainExtraTests
{
    [Fact]
    public void Tenant_cannot_activate_or_suspend_when_inactive()
    {
        var tenant = TestEntities.Tenant();
        tenant.Activate(FixedClock.Now);
        typeof(Tenant).GetProperty(nameof(Tenant.Status))!.SetValue(tenant, TenantStatus.Inactive);
        Assert.Throws<DomainException>(() => tenant.Activate(FixedClock.Now));
        Assert.Throws<DomainException>(() => tenant.Suspend(FixedClock.Now));
    }

    [Fact]
    public void Tenant_activate_and_suspend_from_pending()
    {
        var tenant = TestEntities.Tenant();
        tenant.Activate(FixedClock.Now);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        tenant.Suspend(FixedClock.Now.AddMinutes(1));
        Assert.Equal(TenantStatus.Suspended, tenant.Status);
    }

    [Fact]
    public void Franchisee_reject_and_deactivate()
    {
        var pending = TestEntities.Franchisee();
        pending.Reject(FixedClock.Now);
        Assert.Equal(FranchiseeStatus.Rejected, pending.Status);
        Assert.Throws<DomainException>(() => pending.Deactivate(FixedClock.Now));

        var active = TestEntities.Franchisee(code: "FR-DEACT");
        active.Activate(FixedClock.Now);
        active.Deactivate(FixedClock.Now.AddMinutes(1));
        Assert.Equal(FranchiseeStatus.Inactive, active.Status);
    }

    [Fact]
    public void Store_activate_and_suspend()
    {
        var store = TestEntities.Store();
        store.Activate(FixedClock.Now);
        Assert.Equal(StoreStatus.Active, store.Status);
        store.Suspend(FixedClock.Now.AddMinutes(1));
        Assert.Equal(StoreStatus.Suspended, store.Status);
    }

    [Fact]
    public void Plan_deactivate_blocks_new_subscription()
    {
        var plan = TestEntities.Plan();
        plan.Deactivate(FixedClock.Now);
        Assert.False(plan.IsActive);
        Assert.Throws<DomainException>(() =>
            Subscription.Start(Guid.CreateVersion7(), plan, SubscriptionStatus.Trial, 14, FixedClock.Now));
    }

    [Fact]
    public void Subscription_start_active_uses_monthly_period()
    {
        var now = FixedClock.Now;
        var subscription = Subscription.Start(
            Guid.CreateVersion7(),
            TestEntities.Plan(),
            SubscriptionStatus.Active,
            0,
            now);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(now.AddMonths(1), subscription.CurrentPeriodEnd);
    }

    [Fact]
    public void Subscription_change_plan_and_cancel_rules()
    {
        var subscription = TestEntities.Subscription();
        var other = TestEntities.Plan();
        subscription.ChangePlan(other, FixedClock.Now);
        Assert.Equal(other.Id, subscription.PlanId);

        subscription.Cancel(FixedClock.Now);
        Assert.Throws<DomainException>(() => subscription.Cancel(FixedClock.Now));
        Assert.Throws<DomainException>(() => subscription.ChangePlan(TestEntities.Plan(), FixedClock.Now));
    }

    [Fact]
    public void Subscription_rejects_invalid_initial_status()
    {
        Assert.Throws<DomainException>(() =>
            Subscription.Start(
                Guid.CreateVersion7(),
                TestEntities.Plan(),
                SubscriptionStatus.Suspended,
                14,
                FixedClock.Now));
    }

    [Fact]
    public void Identity_role_helpers()
    {
        Assert.True(Chevrere.Modules.Identity.Domain.RoleNames.IsPlatformRole("PlatformAdmin"));
        Assert.False(Chevrere.Modules.Identity.Domain.RoleNames.IsPlatformRole("FranchiseeOwner"));
        Assert.Contains("FranchiseeOwner", Chevrere.Modules.Identity.Domain.RoleNames.All);
    }
}
