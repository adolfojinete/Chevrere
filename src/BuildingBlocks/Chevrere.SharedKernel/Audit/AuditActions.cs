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
    public const string CategoryCreated = "category.created";
    public const string CategoryUpdated = "category.updated";
    public const string CategoryActivated = "category.activated";
    public const string CategoryDeactivated = "category.deactivated";
    public const string GlobalProductCreated = "global_product.created";
    public const string GlobalProductUpdated = "global_product.updated";
    public const string GlobalProductActivated = "global_product.activated";
    public const string GlobalProductDeactivated = "global_product.deactivated";
    public const string StoreProductEnabled = "store_product.enabled";
    public const string StoreProductDisabled = "store_product.disabled";
}
