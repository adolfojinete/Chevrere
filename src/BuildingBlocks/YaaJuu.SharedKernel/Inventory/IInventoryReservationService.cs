namespace YaaJuu.SharedKernel.Inventory;

/// <summary>
/// Stock reservation port exposed by Inventory to upstream modules (Orders today).
/// Implementations only stage the writes; the caller owns the transaction boundary.
/// </summary>
public interface IInventoryReservationService
{
    Task<IReadOnlyList<InventoryReservationLineResult>> ReserveAsync(
        InventoryReservationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases the exact requested reference set. Validates input, reservation completeness,
    /// inventory items and compatible states before the first mutation. Already Released lines
    /// are idempotent no-ops and do not post a second movement. Does not call SaveChanges.
    /// </summary>
    Task ReleaseAsync(
        InventoryReservationReleaseRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Commits the exact requested reference set with the same validate-then-mutate rules as
    /// <see cref="ReleaseAsync"/>. Already Committed lines are idempotent no-ops. Does not call SaveChanges.
    /// </summary>
    Task CommitAsync(
        InventoryReservationReleaseRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Detaches the pending reservation writes of a failed attempt so a retry or replay
    /// does not persist them later. Never mutates unrelated change tracker entries.
    /// </summary>
    void DiscardPending(IReadOnlyList<InventoryReservationLineResult> applied);
}

public sealed record InventoryReservationRequest(
    Guid TenantId,
    Guid StoreId,
    Guid? ActorUserId,
    string CorrelationId,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<InventoryReservationLine> Lines);

public sealed record InventoryReservationLine(
    Guid GlobalProductId,
    long Quantity,
    string ReferenceType,
    Guid ReferenceId);

public sealed record InventoryReservationReleaseRequest(
    Guid TenantId,
    Guid StoreId,
    Guid? ActorUserId,
    string CorrelationId,
    string ReferenceType,
    IReadOnlyList<Guid> ReferenceIds);

public sealed record InventoryReservationLineResult(
    Guid GlobalProductId,
    Guid ReferenceId,
    Guid ReservationId,
    Guid MovementId,
    Guid InventoryItemId,
    long OnHandAfter,
    long ReservedAfter);
