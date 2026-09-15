using YaaJuu.Modules.Procurement.Application.Abstractions;
using YaaJuu.Modules.Procurement.Application.Contracts;
using YaaJuu.Modules.Procurement.Domain;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Idempotency;
using YaaJuu.SharedKernel.Inventory;
using YaaJuu.SharedKernel.Results;

namespace YaaJuu.Modules.Procurement.Application.Idempotency;

/// <summary>
/// Everything a failed purchase order creation staged locally, so the loser of a concurrent
/// same-key race can be cleaned up without touching unrelated change tracker entries.
/// </summary>
internal sealed record PurchaseOrderAttempt(
    PurchaseOrder Order,
    IdempotentOperation Idempotency,
    string AuditAction,
    Guid AuditEntityId);

/// <summary>
/// Everything a failed goods receipt staged locally: the mutated purchase order, the receipt with
/// its lines, the inventory writes and the idempotency row.
/// </summary>
internal sealed record GoodsReceiptAttempt(
    PurchaseOrder Order,
    GoodsReceipt Receipt,
    IReadOnlyList<InventoryInboundLineResult> Inbound,
    IdempotentOperation Idempotency);

internal static class ProcurementFingerprints
{
    public static string ForPurchaseOrderCreate(
        Guid storeId,
        Guid supplierId,
        string? notes,
        IReadOnlyList<PurchaseOrderItemRequest> items)
    {
        var parts = new List<string?>
        {
            IdempotencyOperations.ProcurementPurchaseOrderCreate,
            storeId.ToString("D"),
            supplierId.ToString("D"),
            notes?.Trim()
        };

        parts.AddRange(items
            .OrderBy(i => i.GlobalProductId)
            .Select(i => string.Join(
                ':',
                i.GlobalProductId.ToString("D"),
                IdempotencyFingerprint.Format(i.OrderedQuantity),
                IdempotencyFingerprint.Format(i.UnitCostAmount),
                i.UnitCostCurrency?.Trim().ToUpperInvariant())));

        return IdempotencyFingerprint.Sha256([.. parts]);
    }

    public static string ForReceive(
        Guid storeId,
        Guid purchaseOrderId,
        string? notes,
        IReadOnlyList<ReceiveGoodsLineRequest> lines)
    {
        var parts = new List<string?>
        {
            IdempotencyOperations.ProcurementReceive,
            storeId.ToString("D"),
            purchaseOrderId.ToString("D"),
            notes?.Trim()
        };

        parts.AddRange(lines
            .OrderBy(l => l.PurchaseOrderItemId)
            .Select(l => string.Join(
                ':',
                l.PurchaseOrderItemId.ToString("D"),
                IdempotencyFingerprint.Format(l.Quantity))));

        return IdempotencyFingerprint.Sha256([.. parts]);
    }
}

/// <summary>
/// Local recovery for concurrent same-key procurement writes: discard the loser attempt, read the
/// committed idempotency winner, and rebuild the response from the persisted document snapshot.
/// </summary>
internal static class ProcurementIdempotencyReplay
{
    public static void DiscardAttempt(
        IProcurementStore store,
        IIdempotencyStore idempotency,
        IAuditRecorder audit,
        PurchaseOrderAttempt attempt)
    {
        store.DiscardPurchaseOrder(attempt.Order);
        idempotency.DiscardPending(attempt.Idempotency);
        audit.DiscardPending(attempt.AuditAction, nameof(PurchaseOrder), attempt.AuditEntityId);
    }

    public static void DiscardAttempt(
        IProcurementStore store,
        IIdempotencyStore idempotency,
        IAuditRecorder audit,
        IInventoryInboundService inventory,
        GoodsReceiptAttempt attempt)
    {
        inventory.DiscardPending(attempt.Inbound);
        store.DiscardGoodsReceipt(attempt.Receipt);
        store.DiscardPurchaseOrder(attempt.Order);
        idempotency.DiscardPending(attempt.Idempotency);
        audit.DiscardPending(AuditActions.GoodsReceiptRecorded, nameof(GoodsReceipt), attempt.Receipt.Id);
    }

    public static async Task<Result<PurchaseOrderDto>?> TryReplayPurchaseOrderAsync(
        IProcurementStore store,
        IIdempotencyStore idempotency,
        IAuditRecorder audit,
        PurchaseOrderAttempt attempt,
        Guid tenantId,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        DiscardAttempt(store, idempotency, audit, attempt);

        var winner = await idempotency.FindAsync(
            tenantId,
            IdempotencyOperations.ProcurementPurchaseOrderCreate,
            idempotencyKey,
            cancellationToken);
        if (winner is null)
        {
            return null;
        }

        if (!string.Equals(winner.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return Result.Failure<PurchaseOrderDto>(
                Error.Conflict("idempotency.key.reused", "Idempotency-Key was already used with a different payload."));
        }

        return await ReplayPurchaseOrderAsync(store, winner.ResourceId, cancellationToken);
    }

    public static async Task<Result<PurchaseOrderDto>> ReplayPurchaseOrderAsync(
        IProcurementStore store,
        Guid? resourceId,
        CancellationToken cancellationToken)
    {
        if (resourceId is null)
        {
            return Result.Failure<PurchaseOrderDto>(
                Error.Conflict(ErrorCodes.Conflict, "Idempotent operation is missing its resource reference."));
        }

        var order = await store.ReadPurchaseOrderAsync(resourceId.Value, cancellationToken);
        if (order is null)
        {
            return Result.Failure<PurchaseOrderDto>(
                Error.Conflict(ErrorCodes.Conflict, "Idempotent purchase order could not be replayed."));
        }

        return Result.Success(ProcurementMapping.ToDto(order));
    }

    public static async Task<Result<GoodsReceiptDto>?> TryReplayGoodsReceiptAsync(
        IProcurementStore store,
        IIdempotencyStore idempotency,
        IAuditRecorder audit,
        IInventoryInboundService inventory,
        GoodsReceiptAttempt attempt,
        Guid tenantId,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        DiscardAttempt(store, idempotency, audit, inventory, attempt);

        var winner = await idempotency.FindAsync(
            tenantId,
            IdempotencyOperations.ProcurementReceive,
            idempotencyKey,
            cancellationToken);
        if (winner is null)
        {
            return null;
        }

        if (!string.Equals(winner.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return Result.Failure<GoodsReceiptDto>(
                Error.Conflict("idempotency.key.reused", "Idempotency-Key was already used with a different payload."));
        }

        return await ReplayGoodsReceiptAsync(store, winner.ResourceId, cancellationToken);
    }

    /// <summary>
    /// Replays from the committed GoodsReceipt, never from the current purchase order state: a later
    /// receipt may have moved the order forward, but the original response must stay identical.
    /// </summary>
    public static async Task<Result<GoodsReceiptDto>> ReplayGoodsReceiptAsync(
        IProcurementStore store,
        Guid? resourceId,
        CancellationToken cancellationToken)
    {
        if (resourceId is null)
        {
            return Result.Failure<GoodsReceiptDto>(
                Error.Conflict(ErrorCodes.Conflict, "Idempotent operation is missing its resource reference."));
        }

        var receipt = await store.ReadGoodsReceiptAsync(resourceId.Value, cancellationToken);
        if (receipt is null)
        {
            return Result.Failure<GoodsReceiptDto>(
                Error.Conflict(ErrorCodes.Conflict, "Idempotent goods receipt could not be replayed."));
        }

        return Result.Success(ProcurementMapping.ToDto(receipt));
    }
}
