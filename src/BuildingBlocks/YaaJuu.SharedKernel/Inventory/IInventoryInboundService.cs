namespace YaaJuu.SharedKernel.Inventory;

/// <summary>
/// Reference types written to the inventory ledger by other modules.
/// </summary>
public static class InventoryReferenceTypes
{
    public const string GoodsReceiptItem = "GoodsReceiptItem";

    public const string InventoryReservation = "InventoryReservation";

    public const string OrderItem = "OrderItem";
}

/// <summary>
/// Inbound stock port exposed by Inventory to upstream modules (Procurement today).
/// Implementations only stage the writes; the caller owns the transaction boundary.
/// </summary>
public interface IInventoryInboundService
{
    Task<IReadOnlyList<InventoryInboundLineResult>> ApplyGoodsReceiptAsync(
        InventoryInboundRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Detaches the pending inventory writes of a failed attempt so a retry or replay
    /// does not persist them later. Never mutates unrelated change tracker entries.
    /// </summary>
    void DiscardPending(IReadOnlyList<InventoryInboundLineResult> applied);
}

public sealed record InventoryInboundRequest(
    Guid TenantId,
    Guid StoreId,
    Guid? ActorUserId,
    string CorrelationId,
    IReadOnlyList<InventoryInboundLine> Lines);

public sealed record InventoryInboundLine(Guid GlobalProductId, Guid GoodsReceiptItemId, long Quantity);

public sealed record InventoryInboundLineResult(
    Guid GlobalProductId,
    Guid GoodsReceiptItemId,
    Guid InventoryItemId,
    Guid MovementId,
    long OnHandAfter,
    long ReservedAfter);
