using Chevrere.SharedKernel.Domain;

namespace Chevrere.Modules.Procurement.Domain;

/// <summary>
/// Purchase order placed on a supplier for one store. Aggregate root of its items:
/// quantities and status only change through this entity.
/// </summary>
public sealed class PurchaseOrder : AggregateRoot
{
    private readonly List<PurchaseOrderItem> _items = [];

    private PurchaseOrder()
    {
        Number = null!;
    }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid SupplierId { get; private set; }

    public string Number { get; private set; }

    public PurchaseOrderStatus Status { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancelReason { get; private set; }

    public IReadOnlyList<PurchaseOrderItem> Items => _items;

    public bool IsDraft => Status == PurchaseOrderStatus.Draft;

    public static PurchaseOrder CreateDraft(
        Guid tenantId,
        Guid storeId,
        Guid supplierId,
        string number,
        string? notes,
        IReadOnlyList<PurchaseOrderLine> lines,
        DateTimeOffset utcNow)
    {
        if (tenantId == Guid.Empty || storeId == Guid.Empty)
        {
            throw new DomainException(
                "purchase_order.identity.required",
                "A purchase order requires a tenant and a store.");
        }

        if (supplierId == Guid.Empty)
        {
            throw new DomainException("purchase_order.supplier.required", "A purchase order requires a supplier.");
        }

        var order = new PurchaseOrder
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            StoreId = storeId,
            SupplierId = supplierId,
            Number = Guard.NotNullOrWhiteSpace(number, nameof(number), 40),
            Status = PurchaseOrderStatus.Draft,
            Notes = NormalizeNotes(notes),
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        order.ReplaceItems(lines, utcNow);
        return order;
    }

    /// <summary>
    /// Replaces supplier, notes and lines of a draft. Only a draft can be edited: once approved the
    /// order is a commitment towards the supplier.
    /// </summary>
    public void UpdateDraft(
        Guid supplierId,
        string? notes,
        IReadOnlyList<PurchaseOrderLine> lines,
        DateTimeOffset utcNow)
    {
        if (!IsDraft)
        {
            throw new DomainException(
                "purchase_order.not_draft",
                "Only a draft purchase order can be edited.");
        }

        if (supplierId == Guid.Empty)
        {
            throw new DomainException("purchase_order.supplier.required", "A purchase order requires a supplier.");
        }

        SupplierId = supplierId;
        Notes = NormalizeNotes(notes);
        ReplaceItems(lines, utcNow);
        UpdatedAt = utcNow;
    }

    /// <returns>true when the order actually moved to Approved.</returns>
    public bool Approve(DateTimeOffset utcNow)
    {
        if (Status == PurchaseOrderStatus.Approved)
        {
            return false;
        }

        if (Status != PurchaseOrderStatus.Draft)
        {
            throw new DomainException(
                "purchase_order.not_approvable",
                $"A purchase order in status {Status} cannot be approved.");
        }

        Status = PurchaseOrderStatus.Approved;
        ApprovedAt = utcNow;
        UpdatedAt = utcNow;
        return true;
    }

    /// <returns>true when the order actually moved to Cancelled.</returns>
    public bool Cancel(string? reason, DateTimeOffset utcNow)
    {
        if (Status == PurchaseOrderStatus.Cancelled)
        {
            return false;
        }

        if (Status is PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received)
        {
            throw new DomainException(
                "purchase_order.not_cancellable",
                "A purchase order with goods already received cannot be cancelled.");
        }

        Status = PurchaseOrderStatus.Cancelled;
        CancelReason = NormalizeNotes(reason);
        CancelledAt = utcNow;
        UpdatedAt = utcNow;
        return true;
    }

    /// <summary>
    /// Applies a goods receipt to the order. Validates every line before mutating anything, so a
    /// rejected receipt leaves the aggregate untouched.
    /// </summary>
    public PurchaseOrderReceipt Receive(IReadOnlyList<GoodsReceiptLine> lines, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived))
        {
            throw new DomainException(
                "purchase_order.not_receivable",
                $"A purchase order in status {Status} cannot receive goods.");
        }

        if (lines.Count == 0)
        {
            throw new DomainException("goods_receipt.lines.required", "A goods receipt requires at least one line.");
        }

        var planned = new List<(PurchaseOrderItem Item, long Quantity)>(lines.Count);
        var seen = new HashSet<Guid>();
        foreach (var line in lines)
        {
            if (!seen.Add(line.PurchaseOrderItemId))
            {
                throw new DomainException(
                    "goods_receipt.line.duplicate",
                    "A goods receipt cannot contain the same purchase order line twice.");
            }

            var item = _items.Find(i => i.Id == line.PurchaseOrderItemId)
                ?? throw new DomainException(
                    "goods_receipt.line.not_found",
                    "One of the lines does not belong to this purchase order.");

            if (line.Quantity <= 0)
            {
                throw new DomainException(
                    "goods_receipt.quantity.invalid",
                    "Received quantity must be greater than zero.");
            }

            if (line.Quantity > item.RemainingQuantity)
            {
                throw new DomainException(
                    "goods_receipt.quantity.exceeds_remaining",
                    $"Cannot receive {line.Quantity} units; only {item.RemainingQuantity} remain on this line.");
            }

            planned.Add((item, line.Quantity));
        }

        var statusBefore = Status;
        var snapshots = new List<GoodsReceiptLineSnapshot>(planned.Count);
        foreach (var (item, quantity) in planned)
        {
            var before = item.ApplyReceipt(quantity, utcNow);
            snapshots.Add(new GoodsReceiptLineSnapshot(
                item.Id,
                item.GlobalProductId,
                quantity,
                before,
                item.ReceivedQuantity,
                item.RemainingQuantity));
        }

        Status = _items.TrueForAll(i => i.IsFullyReceived)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;
        UpdatedAt = utcNow;

        return new PurchaseOrderReceipt(statusBefore, Status, snapshots);
    }

    public bool BelongsTo(Guid tenantId, Guid storeId) => TenantId == tenantId && StoreId == storeId;

    private void ReplaceItems(IReadOnlyList<PurchaseOrderLine> lines, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new DomainException("purchase_order.items.required", "A purchase order requires at least one line.");
        }

        if (lines.DistinctBy(l => l.GlobalProductId).Count() != lines.Count)
        {
            throw new DomainException(
                "purchase_order.item.duplicate",
                "A purchase order cannot contain the same product twice.");
        }

        var currency = lines[0].UnitCost.Currency;
        if (lines.Any(l => !string.Equals(l.UnitCost.Currency, currency, StringComparison.Ordinal)))
        {
            throw new DomainException(
                "purchase_order.currency.mixed",
                "All lines of a purchase order must share the same currency.");
        }

        // A product that stays in the order keeps its line: deleting and re-inserting it would hit
        // the unique (order, product) index inside the same transaction.
        var replacement = new List<PurchaseOrderItem>(lines.Count);
        foreach (var line in lines)
        {
            var existing = _items.Find(i => i.GlobalProductId == line.GlobalProductId);
            if (existing is null)
            {
                replacement.Add(PurchaseOrderItem.Create(Id, TenantId, StoreId, line, utcNow));
                continue;
            }

            existing.Restate(line, utcNow);
            replacement.Add(existing);
        }

        _items.Clear();
        _items.AddRange(replacement);
    }

    private static string? NormalizeNotes(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null : Guard.NotNullOrWhiteSpace(notes, nameof(notes), 1000);
}

/// <summary>
/// Requested receipt line: how many units of a purchase order line arrived.
/// </summary>
public sealed record GoodsReceiptLine(Guid PurchaseOrderItemId, long Quantity);

/// <summary>
/// Immutable result of <see cref="PurchaseOrder.Receive"/>. Carries the exact before/after values a
/// GoodsReceipt must persist so an idempotent replay can reproduce the original response.
/// </summary>
public sealed record PurchaseOrderReceipt(
    PurchaseOrderStatus StatusBefore,
    PurchaseOrderStatus StatusAfter,
    IReadOnlyList<GoodsReceiptLineSnapshot> Lines);

public sealed record GoodsReceiptLineSnapshot(
    Guid PurchaseOrderItemId,
    Guid GlobalProductId,
    long ReceivedQuantity,
    long ReceivedBefore,
    long ReceivedAfter,
    long RemainingAfter);
