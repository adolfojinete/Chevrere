using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.Modules.Procurement.Domain;

/// <summary>
/// Immutable record of goods physically received against a purchase order. Never updated after
/// creation: it is the audit trail that ties a supplier delivery to the inventory ledger, and the
/// snapshot an idempotent replay answers with.
/// </summary>
public sealed class GoodsReceipt : AggregateRoot
{
    private readonly List<GoodsReceiptItem> _items = [];

    private GoodsReceipt()
    {
        ReceiptNumber = null!;
        CorrelationId = null!;
    }

    public Guid TenantId { get; private set; }

    public Guid PurchaseOrderId { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid SupplierId { get; private set; }

    public string ReceiptNumber { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public string? Notes { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string CorrelationId { get; private set; }

    /// <summary>
    /// Status the purchase order reached because of this receipt. Persisted instead of derived so a
    /// replay reports the original outcome even after later receipts moved the order forward.
    /// </summary>
    public PurchaseOrderStatus PurchaseOrderStatusAfter { get; private set; }

    public IReadOnlyList<GoodsReceiptItem> Items => _items;

    public static GoodsReceipt Record(
        PurchaseOrder order,
        string receiptNumber,
        PurchaseOrderReceipt receipt,
        string? notes,
        Guid? actorUserId,
        string correlationId,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.Lines.Count == 0)
        {
            throw new DomainException("goods_receipt.lines.required", "A goods receipt requires at least one line.");
        }

        var result = new GoodsReceipt
        {
            Id = Guid.CreateVersion7(),
            TenantId = order.TenantId,
            PurchaseOrderId = order.Id,
            StoreId = order.StoreId,
            SupplierId = order.SupplierId,
            ReceiptNumber = Guard.NotNullOrWhiteSpace(receiptNumber, nameof(receiptNumber), 40),
            ReceivedAt = utcNow,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.NotNullOrWhiteSpace(notes, nameof(notes), 1000),
            ActorUserId = actorUserId,
            CorrelationId = Guard.NotNullOrWhiteSpace(correlationId, nameof(correlationId), 64),
            PurchaseOrderStatusAfter = receipt.StatusAfter,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        foreach (var line in receipt.Lines)
        {
            result._items.Add(GoodsReceiptItem.Create(result.Id, order.TenantId, order.Id, line, utcNow));
        }

        return result;
    }

    /// <summary>
    /// Links the ledger entry produced by Inventory for one of the received lines.
    /// </summary>
    public void LinkInventoryMovement(Guid goodsReceiptItemId, Guid inventoryMovementId)
    {
        var item = _items.Find(i => i.Id == goodsReceiptItemId)
            ?? throw new DomainException(
                "goods_receipt.item.not_found",
                "The line does not belong to this goods receipt.");

        item.LinkInventoryMovement(inventoryMovementId);
    }
}
