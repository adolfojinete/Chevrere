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
    public const string GlobalSuggestedPriceSet = "global_product_price.set";
    public const string GlobalSuggestedPriceChanged = "global_product_price.changed";
    public const string StorePriceSet = "store_product_price.set";
    public const string StorePriceChanged = "store_product_price.changed";
    public const string StorePriceRemoved = "store_product_price.removed";
    public const string InventoryInitialized = "inventory.initialized";
    public const string InventoryAdjusted = "inventory.adjusted";
    public const string InventoryWasteRecorded = "inventory.waste_recorded";
    public const string SupplierCreated = "supplier.created";
    public const string SupplierUpdated = "supplier.updated";
    public const string SupplierActivated = "supplier.activated";
    public const string SupplierDeactivated = "supplier.deactivated";
    public const string PurchaseOrderCreated = "purchase_order.created";
    public const string PurchaseOrderUpdated = "purchase_order.updated";
    public const string PurchaseOrderApproved = "purchase_order.approved";
    public const string PurchaseOrderCancelled = "purchase_order.cancelled";
    public const string GoodsReceiptRecorded = "goods_receipt.recorded";
    public const string StoreServiceAreaConfigured = "store_service_area.configured";
    public const string StoreServiceAreaEnabled = "store_service_area.enabled";
    public const string StoreServiceAreaDisabled = "store_service_area.disabled";
    public const string OrderCreated = "order.created";
    public const string OrderCancelled = "order.cancelled";
    public const string OrderExpired = "order.expired";
}
