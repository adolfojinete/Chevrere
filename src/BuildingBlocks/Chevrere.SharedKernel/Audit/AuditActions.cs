namespace Chevrere.SharedKernel.Audit;

public static class AuditActions
{
    public const string TenantCreated = "tenant.created";
    public const string FranchiseeCreated = "franchisee.created";
    public const string FranchiseeActivated = "franchisee.activated";
    public const string FranchiseeSuspended = "franchisee.suspended";
    public const string FranchiseeReactivated = "franchisee.reactivated";
    public const string StoreCreated = "store.created";
    public const string OwnerCreated = "user.owner.created";
    public const string SubscriptionCreated = "subscription.created";
    public const string SubscriptionSuspended = "subscription.suspended";
    public const string SubscriptionReactivated = "subscription.reactivated";
    public const string PlanChanged = "subscription.plan_changed";
}
