using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.Modules.Subscriptions.Domain;

public sealed class Subscription : AggregateRoot
{
    private Subscription()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid PlanId { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public DateTimeOffset StartDate { get; private set; }

    public DateTimeOffset CurrentPeriodStart { get; private set; }

    public DateTimeOffset CurrentPeriodEnd { get; private set; }

    public DateTimeOffset NextBillingDate { get; private set; }

    public DateTimeOffset? GracePeriodUntil { get; private set; }

    public DateTimeOffset? SuspendedAt { get; private set; }

    public string? SuspensionReason { get; private set; }

    public static Subscription Start(
        Guid tenantId,
        Plan plan,
        SubscriptionStatus initialStatus,
        int trialDays,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (tenantId == Guid.Empty)
        {
            throw new DomainException("subscription.tenant.required", "Subscription requires a tenant.");
        }

        if (!plan.IsActive)
        {
            throw new DomainException("plan.inactive", "Cannot subscribe to an inactive plan.");
        }

        if (initialStatus is not (SubscriptionStatus.Trial or SubscriptionStatus.Active))
        {
            throw new DomainException(
                "subscription.initial_status.invalid",
                "A new subscription can only start as Trial or Active.");
        }

        if (initialStatus is SubscriptionStatus.Trial && trialDays < 0)
        {
            throw new DomainException("subscription.trial.invalid", "Trial days cannot be negative.");
        }

        var periodEnd = initialStatus is SubscriptionStatus.Trial && trialDays > 0
            ? utcNow.AddDays(trialDays)
            : utcNow.AddMonths(1);

        return new Subscription
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            PlanId = plan.Id,
            Status = initialStatus,
            StartDate = utcNow,
            CurrentPeriodStart = utcNow,
            CurrentPeriodEnd = periodEnd,
            NextBillingDate = periodEnd,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public void MarkPastDue(DateTimeOffset utcNow)
    {
        EnsureTransition(SubscriptionStatus.PastDue, SubscriptionStatus.Active, SubscriptionStatus.Trial, SubscriptionStatus.GracePeriod);
        Status = SubscriptionStatus.PastDue;
        UpdatedAt = utcNow;
    }

    public void EnterGracePeriod(DateTimeOffset utcNow, int gracePeriodDays)
    {
        if (gracePeriodDays < 0)
        {
            throw new DomainException("subscription.grace.invalid", "Grace period days cannot be negative.");
        }

        EnsureTransition(SubscriptionStatus.GracePeriod, SubscriptionStatus.PastDue, SubscriptionStatus.Active);
        Status = SubscriptionStatus.GracePeriod;
        GracePeriodUntil = utcNow.AddDays(gracePeriodDays);
        UpdatedAt = utcNow;
    }

    public void Suspend(DateTimeOffset utcNow, string reason)
    {
        if (Status is SubscriptionStatus.Cancelled)
        {
            throw new DomainException("subscription.cancelled", "A cancelled subscription cannot be suspended.");
        }

        if (Status is SubscriptionStatus.Suspended)
        {
            throw new DomainException("subscription.already_suspended", "The subscription is already suspended.");
        }

        Status = SubscriptionStatus.Suspended;
        SuspendedAt = utcNow;
        SuspensionReason = Guard.NotNullOrWhiteSpace(reason, nameof(reason), 500);
        UpdatedAt = utcNow;
    }

    public void Reactivate(DateTimeOffset utcNow)
    {
        if (Status is not SubscriptionStatus.Suspended)
        {
            throw new DomainException(
                "subscription.invalid_transition",
                $"Cannot reactivate a subscription in status {Status}.");
        }

        Status = SubscriptionStatus.Active;
        SuspendedAt = null;
        SuspensionReason = null;
        GracePeriodUntil = null;
        UpdatedAt = utcNow;
    }

    public void Cancel(DateTimeOffset utcNow)
    {
        if (Status is SubscriptionStatus.Cancelled)
        {
            throw new DomainException("subscription.already_cancelled", "The subscription is already cancelled.");
        }

        Status = SubscriptionStatus.Cancelled;
        UpdatedAt = utcNow;
    }

    public void ChangePlan(Plan plan, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.IsActive)
        {
            throw new DomainException("plan.inactive", "Cannot change to an inactive plan.");
        }

        if (Status is SubscriptionStatus.Cancelled)
        {
            throw new DomainException("subscription.cancelled", "Cannot change the plan of a cancelled subscription.");
        }

        PlanId = plan.Id;
        UpdatedAt = utcNow;
    }

    public bool BelongsTo(Guid tenantId) => TenantId == tenantId;

    private void EnsureTransition(SubscriptionStatus target, params SubscriptionStatus[] allowedSources)
    {
        if (!allowedSources.Contains(Status))
        {
            throw new DomainException(
                "subscription.invalid_transition",
                $"Cannot change subscription from {Status} to {target}.");
        }
    }
}
