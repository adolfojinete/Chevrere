using YaaJuu.Modules.Procurement.Domain;

namespace YaaJuu.Modules.Procurement.Application.Abstractions;

/// <summary>
/// Read-only checks Procurement needs about entities owned by other modules. Keeps Procurement free
/// of project references to Tenancy and Catalog.
/// </summary>
public interface IProcurementStoreAccess
{
    Task<Guid?> GetStoreTenantIdAsync(Guid storeId, CancellationToken cancellationToken);

    Task<bool> StoreProductExistsAsync(
        Guid tenantId,
        Guid storeId,
        Guid globalProductId,
        CancellationToken cancellationToken);

    Task<bool> GlobalProductIsActiveAsync(Guid globalProductId, CancellationToken cancellationToken);
}

/// <summary>
/// Document numbers are drawn from PostgreSQL sequences so concurrent requests never collide and a
/// rolled back transaction does not reuse a number.
/// </summary>
public interface IDocumentNumberGenerator
{
    Task<string> NextPurchaseOrderNumberAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<string> NextGoodsReceiptNumberAsync(Guid tenantId, CancellationToken cancellationToken);
}

public interface IProcurementStore
{
    Task<Supplier?> GetSupplierAsync(Guid supplierId, CancellationToken cancellationToken);

    Task<bool> SupplierCodeExistsAsync(Guid tenantId, string code, CancellationToken cancellationToken);

    void AddSupplier(Supplier supplier);

    Task<(IReadOnlyList<Supplier> Items, int Total)> ListSuppliersAsync(
        Guid tenantId,
        int page,
        int pageSize,
        string? search,
        bool? isActive,
        CancellationToken cancellationToken);

    void AddPurchaseOrder(PurchaseOrder order);

    /// <summary>Tracked load, items included, for mutations.</summary>
    Task<PurchaseOrder?> GetPurchaseOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken);

    /// <summary>Untracked load, items included, for queries and idempotent replays.</summary>
    Task<PurchaseOrder?> ReadPurchaseOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<PurchaseOrderProjection> Items, int Total)> ListPurchaseOrdersAsync(
        Guid tenantId,
        Guid storeId,
        int page,
        int pageSize,
        PurchaseOrderStatus? status,
        Guid? supplierId,
        CancellationToken cancellationToken);

    void AddGoodsReceipt(GoodsReceipt receipt);

    /// <summary>Untracked load, items included, for queries and idempotent replays.</summary>
    Task<GoodsReceipt?> ReadGoodsReceiptAsync(Guid goodsReceiptId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<GoodsReceiptProjection> Items, int Total)> ListGoodsReceiptsAsync(
        Guid tenantId,
        Guid purchaseOrderId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>Detaches a failed purchase order attempt, including its lines.</summary>
    void DiscardPurchaseOrder(PurchaseOrder order);

    /// <summary>Detaches a failed goods receipt attempt, including its lines.</summary>
    void DiscardGoodsReceipt(GoodsReceipt receipt);
}

public sealed record PurchaseOrderProjection(
    Guid Id,
    Guid StoreId,
    Guid SupplierId,
    string Number,
    PurchaseOrderStatus Status,
    int LineCount,
    long OrderedQuantity,
    long ReceivedQuantity,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GoodsReceiptProjection(
    Guid Id,
    Guid PurchaseOrderId,
    string ReceiptNumber,
    DateTimeOffset ReceivedAt,
    PurchaseOrderStatus PurchaseOrderStatusAfter,
    int LineCount,
    long ReceivedQuantity);
