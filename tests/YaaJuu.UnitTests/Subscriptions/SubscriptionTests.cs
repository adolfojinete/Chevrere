using YaaJuu.Modules.Subscriptions.Domain;
using YaaJuu.Modules.Subscriptions.Domain.ValueObjects;
using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.UnitTests.Subscriptions;

public sealed class SubscriptionTests
{
    [Fact]
    public void Rename_updates_display_name_without_changing_plan_identity()
    {
        var plan = TestEntities.Plan();
        var id = plan.Id;

        plan.Rename("Chevrere Standard", FixedClock.Now);

        Assert.Equal(id, plan.Id);
        Assert.Equal("STANDARD", plan.Code);
        Assert.Equal("Chevrere Standard", plan.Name);

        plan.Rename("YaaJuu Standard", FixedClock.Now.AddMinutes(1));

        Assert.Equal(id, plan.Id);
        Assert.Equal("STANDARD", plan.Code);
        Assert.Equal("YaaJuu Standard", plan.Name);
    }

    [Fact]
    public void Start_trial_sets_period_from_trial_days()
    {
        var plan = TestEntities.Plan();
        var tenantId = Guid.CreateVersion7();
        var now = FixedClock.Now;

        var subscription = Subscription.Start(tenantId, plan, SubscriptionStatus.Trial, 14, now);

        Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
        Assert.Equal(tenantId, subscription.TenantId);
        Assert.Equal(now.AddDays(14), subscription.CurrentPeriodEnd);
        Assert.True(subscription.BelongsTo(tenantId));
    }

    [Fact]
    public void Cannot_start_without_tenant()
    {
        Assert.Throws<DomainException>(() =>
            Subscription.Start(Guid.Empty, TestEntities.Plan(), SubscriptionStatus.Trial, 14, FixedClock.Now));
    }

    [Fact]
    public void Suspend_does_not_clear_identity()
    {
        var subscription = TestEntities.Subscription();
        var id = subscription.Id;
        var tenantId = subscription.TenantId;

        subscription.Suspend(FixedClock.Now, "Past due");

        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
        Assert.Equal(id, subscription.Id);
        Assert.Equal(tenantId, subscription.TenantId);
        Assert.Equal("Past due", subscription.SuspensionReason);
    }

    [Fact]
    public void Reactivate_from_suspended_is_valid()
    {
        var subscription = TestEntities.Subscription();
        subscription.Suspend(FixedClock.Now, "Admin");
        subscription.Reactivate(FixedClock.Now.AddMinutes(1));
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Null(subscription.SuspensionReason);
    }

    [Fact]
    public void Reactivate_from_trial_is_invalid()
    {
        var subscription = TestEntities.Subscription();
        Assert.Throws<DomainException>(() => subscription.Reactivate(FixedClock.Now));
    }

    [Fact]
    public void Cancelled_subscription_cannot_be_suspended()
    {
        var subscription = TestEntities.Subscription();
        subscription.Cancel(FixedClock.Now);
        Assert.Throws<DomainException>(() => subscription.Suspend(FixedClock.Now, "x"));
    }

    [Fact]
    public void Past_due_from_active_is_valid()
    {
        var plan = TestEntities.Plan();
        var subscription = Subscription.Start(Guid.CreateVersion7(), plan, SubscriptionStatus.Active, 0, FixedClock.Now);
        subscription.MarkPastDue(FixedClock.Now);
        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
    }

    [Fact]
    public void Grace_period_from_cancelled_is_invalid()
    {
        var subscription = TestEntities.Subscription();
        subscription.Cancel(FixedClock.Now);
        Assert.Throws<DomainException>(() => subscription.EnterGracePeriod(FixedClock.Now, 7));
    }

    [Fact]
    public void Grace_period_from_past_due_is_valid()
    {
        var subscription = TestEntities.Subscription();
        subscription.MarkPastDue(FixedClock.Now);
        subscription.EnterGracePeriod(FixedClock.Now, 7);
        Assert.Equal(SubscriptionStatus.GracePeriod, subscription.Status);
        Assert.Equal(FixedClock.Now.AddDays(7), subscription.GracePeriodUntil);
    }

    [Fact]
    public void Negative_grace_or_trial_is_rejected()
    {
        var subscription = TestEntities.Subscription();
        subscription.MarkPastDue(FixedClock.Now);
        Assert.Throws<DomainException>(() => subscription.EnterGracePeriod(FixedClock.Now, -1));
        Assert.Throws<DomainException>(() =>
            Subscription.Start(Guid.CreateVersion7(), TestEntities.Plan(), SubscriptionStatus.Trial, -1, FixedClock.Now));
    }

    [Fact]
    public void Cannot_suspend_twice_or_change_to_inactive_plan()
    {
        var subscription = TestEntities.Subscription();
        subscription.Suspend(FixedClock.Now, "once");
        Assert.Throws<DomainException>(() => subscription.Suspend(FixedClock.Now, "twice"));

        var inactive = TestEntities.Plan();
        inactive.Deactivate(FixedClock.Now);
        var active = TestEntities.Subscription();
        Assert.Throws<DomainException>(() => active.ChangePlan(inactive, FixedClock.Now));
    }
}
