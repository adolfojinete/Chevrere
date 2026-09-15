using YaaJuu.Modules.Inventory.Application.Abstractions;
using YaaJuu.Modules.Inventory.Application.Contracts;
using YaaJuu.Modules.Inventory.Domain;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Idempotency;
using YaaJuu.SharedKernel.Results;

namespace YaaJuu.Modules.Inventory.Application.Idempotency;

internal sealed record InventoryMutationAttempt(
    InventoryItem Item,
    InventoryMovement? Movement,
    IdempotentOperation Idempotency,
    string AuditAction,
    string AuditEntityType,
    Guid AuditEntityId);

/// <summary>
/// Local recovery for concurrent same-key inventory mutations: discard loser attempt entries,
/// read committed idempotency winner, rebuild response from movement snapshot when possible.
/// </summary>
internal static class InventoryIdempotencyReplay
{
    public static void DiscardAttempt(
        IInventoryStore store,
        IIdempotencyStore idempotency,
        IAuditRecorder audit,
        InventoryMutationAttempt attempt)
    {
        if (attempt.Movement is not null)
        {
            store.DiscardMovement(attempt.Movement);
        }

        store.DiscardItem(attempt.Item);
        idempotency.DiscardPending(attempt.Idempotency);
        audit.DiscardPending(attempt.AuditAction, attempt.AuditEntityType, attempt.AuditEntityId);
    }

    public static async Task<Result<InventoryMutationDto>?> TryReplayAfterWriteConflictAsync(
        IInventoryStore store,
        IIdempotencyStore idempotency,
        IAuditRecorder audit,
        InventoryMutationAttempt attempt,
        Guid tenantId,
        string operation,
        string idempotencyKey,
        string requestHash,
        long? initializeQuantity,
        CancellationToken cancellationToken)
    {
        DiscardAttempt(store, idempotency, audit, attempt);

        var winner = await idempotency.FindAsync(tenantId, operation, idempotencyKey, cancellationToken);
        if (winner is null)
        {
            return null;
        }

        if (!string.Equals(winner.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return Result.Failure<InventoryMutationDto>(
                Error.Conflict("idempotency.key.reused", "Idempotency-Key was already used with a different payload."));
        }

        return await ReplayFromWinnerAsync(store, operation, winner.ResourceId, initializeQuantity, cancellationToken);
    }

    public static async Task<Result<InventoryMutationDto>> ReplayFromWinnerAsync(
        IInventoryStore store,
        string operation,
        Guid? resourceId,
        long? initializeQuantity,
        CancellationToken cancellationToken)
    {
        if (resourceId is null)
        {
            return Result.Failure<InventoryMutationDto>(
                Error.Conflict(ErrorCodes.Conflict, "Idempotent operation is missing its resource reference."));
        }

        if (operation == IdempotencyOperations.InventoryInitialize && initializeQuantity is 0)
        {
            return Result.Success(new InventoryMutationDto(resourceId.Value, null, 0, 0, 0));
        }

        var movement = await store.GetMovementAsync(resourceId.Value, cancellationToken);
        if (movement is null)
        {
            return Result.Failure<InventoryMutationDto>(
                Error.Conflict(ErrorCodes.Conflict, "Idempotent inventory mutation could not be replayed."));
        }

        return Result.Success(FromMovement(movement));
    }

    public static InventoryMutationDto FromMovement(InventoryMovement movement) =>
        new(
            movement.InventoryItemId,
            movement.Id,
            movement.OnHandAfter,
            movement.ReservedAfter,
            movement.OnHandAfter - movement.ReservedAfter);

    public static InventoryMutationDto FromItem(InventoryItem item, Guid? movementId) =>
        new(item.Id, movementId, item.OnHand, item.Reserved, item.Available);
}
