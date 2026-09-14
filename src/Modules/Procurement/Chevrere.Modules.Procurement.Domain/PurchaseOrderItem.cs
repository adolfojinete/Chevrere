using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Domain.ValueObjects;

namespace Chevrere.Modules.Procurement.Domain;

/// <summary>
/// One ordered product inside a purchase order. ReceivedQuantity only ever grows and never exceeds
/// OrderedQuantity; over-receipt is rejected in the domain and again by a database check constraint.
/// </summary>
public sealed class PurchaseOrderItem : Entity
{
    private PurchaseOrderItem()
    {
        UnitCostCurrency = null!;
    }

    public Guid PurchaseOrderId { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid GlobalProductId { get; private set; }

    public long OrderedQuantity { get; private set; }

    public long ReceivedQuantity { get; private set; }

    public decimal UnitCostAmount { get; private set; }

    public string UnitCostCurrency { get; private set; }

    public long RemainingQuantity => OrderedQuantity - ReceivedQuantity;

    public bool IsFullyReceived => RemainingQuantity == 0;

    public Money UnitCost() => Money.FromPersistence(UnitCostAmount, UnitCostCurrency);

    internal static PurchaseOrderItem Create(
        Guid purchaseOrderId,
        Guid tenantId,
        Guid storeId,
        PurchaseOrderLine line,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.GlobalProductId == Guid.Empty)
        {
            throw new DomainException("purchase_order.item.product.required", "Every line requires a product.");
        }

        if (line.OrderedQuantity <= 0)
        {
            throw new DomainException(
                "purchase_order.item.quantity.invalid",
                "Ordered quantity must be greater than zero.");
        }

        return new PurchaseOrderItem
        {
            Id = Guid.CreateVersion7(),
            PurchaseOrderId = purchaseOrderId,
            TenantId = tenantId,
            StoreId = storeId,
            GlobalProductId = line.GlobalProductId,
            OrderedQuantity = line.OrderedQuantity,
            ReceivedQuantity = 0,
            UnitCostAmount = line.UnitCost.Amount,
            UnitCostCurrency = line.UnitCost.Currency,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    /// <summary>
    /// Re-states an existing draft line. The row is reused instead of replaced so editing a draft
    /// never deletes and re-inserts the same (order, product) pair inside one transaction.
    /// </summary>
    internal void Restate(PurchaseOrderLine line, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.OrderedQuantity <= 0)
        {
            throw new DomainException(
                "purchase_order.item.quantity.invalid",
                "Ordered quantity must be greater than zero.");
        }

        if (line.OrderedQuantity < ReceivedQuantity)
        {
            throw new DomainException(
                "purchase_order.item.quantity.below_received",
                "Ordered quantity cannot be lower than the quantity already received.");
        }

        OrderedQuantity = line.OrderedQuantity;
        UnitCostAmount = line.UnitCost.Amount;
        UnitCostCurrency = line.UnitCost.Currency;
        UpdatedAt = utcNow;
    }

    /// <returns>ReceivedQuantity before applying the receipt.</returns>
    internal long ApplyReceipt(long quantity, DateTimeOffset utcNow)
    {
        if (quantity <= 0)
        {
            throw new DomainException(
                "goods_receipt.quantity.invalid",
                "Received quantity must be greater than zero.");
        }

        if (quantity > RemainingQuantity)
        {
            throw new DomainException(
                "goods_receipt.quantity.exceeds_remaining",
                $"Cannot receive {quantity} units; only {RemainingQuantity} remain on this line.");
        }

        var before = ReceivedQuantity;
        ReceivedQuantity = checked(ReceivedQuantity + quantity);
        UpdatedAt = utcNow;
        return before;
    }
}

/// <summary>
/// Requested line of a purchase order draft.
/// </summary>
public sealed record PurchaseOrderLine(Guid GlobalProductId, long OrderedQuantity, Money UnitCost);
