using Chevrere.Modules.Procurement.Application.Abstractions;
using Chevrere.Modules.Procurement.Domain;

namespace Chevrere.Modules.Procurement.Application.Contracts;

internal static class ProcurementMapping
{
    public static SupplierDto ToDto(Supplier supplier) =>
        new(
            supplier.Id,
            supplier.Code,
            supplier.Name,
            supplier.TaxIdentification,
            supplier.ContactName,
            supplier.Email,
            supplier.Phone,
            supplier.Address,
            supplier.Notes,
            supplier.IsActive,
            supplier.CreatedAt,
            supplier.UpdatedAt);

    public static PurchaseOrderDto ToDto(PurchaseOrder order) =>
        new(
            order.Id,
            order.StoreId,
            order.SupplierId,
            order.Number,
            order.Status,
            order.Notes,
            order.ApprovedAt,
            order.CancelledAt,
            order.CancelReason,
            order.CreatedAt,
            order.UpdatedAt,
            order.Items
                .OrderBy(i => i.Id)
                .Select(i => new PurchaseOrderItemDto(
                    i.Id,
                    i.GlobalProductId,
                    i.OrderedQuantity,
                    i.ReceivedQuantity,
                    i.RemainingQuantity,
                    i.UnitCostAmount,
                    i.UnitCostCurrency))
                .ToList());

    public static PurchaseOrderSummaryDto ToDto(PurchaseOrderProjection projection) =>
        new(
            projection.Id,
            projection.StoreId,
            projection.SupplierId,
            projection.Number,
            projection.Status,
            projection.LineCount,
            projection.OrderedQuantity,
            projection.ReceivedQuantity,
            projection.CreatedAt,
            projection.UpdatedAt);

    public static GoodsReceiptDto ToDto(GoodsReceipt receipt) =>
        new(
            receipt.Id,
            receipt.PurchaseOrderId,
            receipt.StoreId,
            receipt.SupplierId,
            receipt.ReceiptNumber,
            receipt.ReceivedAt,
            receipt.Notes,
            receipt.ActorUserId,
            receipt.PurchaseOrderStatusAfter,
            receipt.Items
                .OrderBy(i => i.Id)
                .Select(i => new GoodsReceiptItemDto(
                    i.Id,
                    i.PurchaseOrderItemId,
                    i.GlobalProductId,
                    i.ReceivedQuantity,
                    i.ReceivedBefore,
                    i.ReceivedAfter,
                    i.RemainingAfter,
                    i.InventoryMovementId))
                .ToList());

    public static GoodsReceiptSummaryDto ToDto(GoodsReceiptProjection projection) =>
        new(
            projection.Id,
            projection.PurchaseOrderId,
            projection.ReceiptNumber,
            projection.ReceivedAt,
            projection.PurchaseOrderStatusAfter,
            projection.LineCount,
            projection.ReceivedQuantity);
}
