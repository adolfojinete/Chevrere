using Chevrere.Modules.Procurement.Domain;

namespace Chevrere.Modules.Procurement.Application.Contracts;

public sealed record CreateSupplierRequest(
    string Code,
    string Name,
    string? TaxIdentification,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Address,
    string? Notes);

public sealed record UpdateSupplierRequest(
    string Name,
    string? TaxIdentification,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Address,
    string? Notes);

public sealed record SupplierDto(
    Guid Id,
    string Code,
    string Name,
    string? TaxIdentification,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Address,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PurchaseOrderItemRequest(
    Guid GlobalProductId,
    long OrderedQuantity,
    decimal UnitCostAmount,
    string UnitCostCurrency);

public sealed record CreatePurchaseOrderRequest(
    Guid SupplierId,
    string? Notes,
    IReadOnlyList<PurchaseOrderItemRequest> Items);

public sealed record UpdatePurchaseOrderRequest(
    Guid SupplierId,
    string? Notes,
    IReadOnlyList<PurchaseOrderItemRequest> Items);

public sealed record CancelPurchaseOrderRequest(string? Reason);

public sealed record PurchaseOrderItemDto(
    Guid Id,
    Guid GlobalProductId,
    long OrderedQuantity,
    long ReceivedQuantity,
    long RemainingQuantity,
    decimal UnitCostAmount,
    string UnitCostCurrency);

public sealed record PurchaseOrderDto(
    Guid Id,
    Guid StoreId,
    Guid SupplierId,
    string Number,
    PurchaseOrderStatus Status,
    string? Notes,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PurchaseOrderItemDto> Items);

public sealed record PurchaseOrderSummaryDto(
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

public sealed record ReceiveGoodsLineRequest(Guid PurchaseOrderItemId, long Quantity);

public sealed record ReceiveGoodsRequest(string? Notes, IReadOnlyList<ReceiveGoodsLineRequest> Lines);

public sealed record GoodsReceiptItemDto(
    Guid Id,
    Guid PurchaseOrderItemId,
    Guid GlobalProductId,
    long ReceivedQuantity,
    long ReceivedBefore,
    long ReceivedAfter,
    long RemainingAfter,
    Guid? InventoryMovementId);

public sealed record GoodsReceiptDto(
    Guid Id,
    Guid PurchaseOrderId,
    Guid StoreId,
    Guid SupplierId,
    string ReceiptNumber,
    DateTimeOffset ReceivedAt,
    string? Notes,
    Guid? ActorUserId,
    PurchaseOrderStatus PurchaseOrderStatusAfter,
    IReadOnlyList<GoodsReceiptItemDto> Items);

public sealed record GoodsReceiptSummaryDto(
    Guid Id,
    Guid PurchaseOrderId,
    string ReceiptNumber,
    DateTimeOffset ReceivedAt,
    PurchaseOrderStatus PurchaseOrderStatusAfter,
    int LineCount,
    long ReceivedQuantity);
