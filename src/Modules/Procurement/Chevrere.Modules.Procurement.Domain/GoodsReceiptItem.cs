using Chevrere.SharedKernel.Domain;

namespace Chevrere.Modules.Procurement.Domain;

/// <summary>
/// One received line of a goods receipt, with the purchase order line snapshot at the moment of
/// reception and the inventory movement it produced.
/// </summary>
public sealed class GoodsReceiptItem : Entity
{
    private GoodsReceiptItem()
    {
    }

    public Guid GoodsReceiptId { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>
    /// Denormalized from the parent receipt so PostgreSQL can prove this line belongs to the same
    /// purchase order as both the receipt and the referenced purchase-order item.
    /// </summary>
    public Guid PurchaseOrderId { get; private set; }

    public Guid PurchaseOrderItemId { get; private set; }

    public Guid GlobalProductId { get; private set; }

    public long ReceivedQuantity { get; private set; }

    public long ReceivedBefore { get; private set; }

    public long ReceivedAfter { get; private set; }

    public long RemainingAfter { get; private set; }

    public Guid? InventoryMovementId { get; private set; }

    internal static GoodsReceiptItem Create(
        Guid goodsReceiptId,
        Guid tenantId,
        Guid purchaseOrderId,
        GoodsReceiptLineSnapshot snapshot,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new GoodsReceiptItem
        {
            Id = Guid.CreateVersion7(),
            GoodsReceiptId = goodsReceiptId,
            TenantId = tenantId,
            PurchaseOrderId = purchaseOrderId,
            PurchaseOrderItemId = snapshot.PurchaseOrderItemId,
            GlobalProductId = snapshot.GlobalProductId,
            ReceivedQuantity = snapshot.ReceivedQuantity,
            ReceivedBefore = snapshot.ReceivedBefore,
            ReceivedAfter = snapshot.ReceivedAfter,
            RemainingAfter = snapshot.RemainingAfter,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    internal void LinkInventoryMovement(Guid inventoryMovementId)
    {
        if (inventoryMovementId == Guid.Empty)
        {
            throw new DomainException(
                "goods_receipt.movement.required",
                "A received line requires a valid inventory movement.");
        }

        if (InventoryMovementId is not null)
        {
            throw new DomainException(
                "goods_receipt.movement.already_linked",
                "The received line is already linked to an inventory movement.");
        }

        InventoryMovementId = inventoryMovementId;
    }
}
