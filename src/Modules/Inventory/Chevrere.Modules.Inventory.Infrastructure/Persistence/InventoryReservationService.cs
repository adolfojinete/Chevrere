using Chevrere.Modules.Inventory.Application.Abstractions;
using Chevrere.Modules.Inventory.Domain;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Inventory;
using Chevrere.SharedKernel.Time;

namespace Chevrere.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// Holds, releases or commits stock for another module's document line. Stages reservations,
/// balances and ledger entries in the caller's unit of work; the caller commits everything at once.
/// </summary>
public sealed class InventoryReservationService(IInventoryStore store, IClock clock) : IInventoryReservationService
{
    private readonly Dictionary<Guid, StagedWrite> _staged = [];

    public async Task<IReadOnlyList<InventoryReservationLineResult>> ReserveAsync(
        InventoryReservationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Lines.Count == 0)
        {
            throw new DomainException(
                "inventory.reservation.lines.required",
                "A reservation request requires at least one line.");
        }

        if (request.Lines.DistinctBy(l => l.GlobalProductId).Count() != request.Lines.Count)
        {
            throw new DomainException(
                "inventory.reservation.product.duplicate",
                "A reservation request cannot contain the same product twice.");
        }

        if (request.Lines.DistinctBy(l => l.ReferenceId).Count() != request.Lines.Count)
        {
            throw new DomainException(
                InventoryReservationErrors.ReferenceDuplicate,
                "A reservation request cannot contain the same reference twice.");
        }

        var utcNow = clock.UtcNow;
        var ordered = request.Lines
            .OrderBy(l => l.GlobalProductId)
            .ThenBy(l => l.ReferenceId)
            .ToList();

        var items = await store.GetItemsByProductsAsync(
            request.TenantId,
            request.StoreId,
            [.. ordered.Select(l => l.GlobalProductId)],
            cancellationToken);
        var itemsByProduct = items.ToDictionary(i => i.GlobalProductId);

        foreach (var line in ordered)
        {
            if (line.Quantity <= 0)
            {
                throw new DomainException(
                    "inventory.reservation.quantity.invalid",
                    "Reservation quantity must be greater than zero.");
            }

            if (!itemsByProduct.TryGetValue(line.GlobalProductId, out var item))
            {
                throw new DomainException(
                    "inventory.insufficient_available",
                    "Available stock is not enough for this quantity.");
            }

            if (line.Quantity > item.Available)
            {
                throw new DomainException(
                    "inventory.insufficient_available",
                    "Available stock is not enough for this quantity.");
            }
        }

        var results = new List<InventoryReservationLineResult>(ordered.Count);
        foreach (var line in ordered)
        {
            var item = itemsByProduct[line.GlobalProductId];
            var reservation = InventoryReservation.Open(
                request.TenantId,
                request.StoreId,
                item.Id,
                line.GlobalProductId,
                line.ReferenceType,
                line.ReferenceId,
                line.Quantity,
                request.ExpiresAt,
                utcNow);
            var movement = item.Reserve(
                line.Quantity,
                reservation.Id,
                request.ActorUserId,
                request.CorrelationId,
                utcNow);
            store.AddReservation(reservation);
            store.AddMovement(movement);
            _staged[movement.Id] = new StagedWrite(item, movement, reservation);
            results.Add(new InventoryReservationLineResult(
                line.GlobalProductId,
                line.ReferenceId,
                reservation.Id,
                movement.Id,
                item.Id,
                movement.OnHandAfter,
                movement.ReservedAfter));
        }

        return results;
    }

    public Task ReleaseAsync(
        InventoryReservationReleaseRequest request,
        CancellationToken cancellationToken) =>
        CompleteAsync(request, commit: false, cancellationToken);

    public Task CommitAsync(
        InventoryReservationReleaseRequest request,
        CancellationToken cancellationToken) =>
        CompleteAsync(request, commit: true, cancellationToken);

    public void DiscardPending(IReadOnlyList<InventoryReservationLineResult> applied)
    {
        ArgumentNullException.ThrowIfNull(applied);

        foreach (var result in applied)
        {
            if (!_staged.Remove(result.MovementId, out var staged))
            {
                continue;
            }

            store.DiscardMovement(staged.Movement);
            store.DiscardReservation(staged.Reservation);
            store.DiscardItem(staged.Item);
        }
    }

    /// <summary>
    /// Two-phase completion: validate the exact reservation set, every InventoryItem and every
    /// status first; only then transition, update balances and post ledger movements.
    /// Already Released (on release) or already Committed (on commit) lines are retry no-ops
    /// and do not post a second movement. Any missing reference, missing item, identity mismatch
    /// or incompatible status fails the whole batch with zero mutations. Does not call SaveChanges.
    /// </summary>
    private async Task CompleteAsync(
        InventoryReservationReleaseRequest request,
        bool commit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ReferenceIds);
        if (request.ReferenceIds.Count == 0)
        {
            return;
        }

        var requested = UniqueReferenceIds(request.ReferenceIds);
        var reservations = await store.GetReservationsByReferencesAsync(
            request.TenantId,
            request.StoreId,
            request.ReferenceType,
            request.ReferenceIds,
            cancellationToken);
        EnsureExactReservationSet(requested, reservations);

        var items = await store.GetItemsByProductsAsync(
            request.TenantId,
            request.StoreId,
            [.. reservations.Select(r => r.GlobalProductId).Distinct()],
            cancellationToken);
        var itemsById = EnsureInventoryItems(reservations, items);
        EnsureCompatibleStates(reservations, commit);

        var utcNow = clock.UtcNow;
        foreach (var reservation in reservations.OrderBy(r => r.GlobalProductId).ThenBy(r => r.Id))
        {
            var changed = commit ? reservation.Commit(utcNow) : reservation.Release(utcNow);
            if (!changed)
            {
                continue;
            }

            var item = itemsById[reservation.InventoryItemId];
            var movement = commit
                ? item.CommitReservation(
                    reservation.Quantity, reservation.Id, request.ActorUserId, request.CorrelationId, utcNow)
                : item.ReleaseReservation(
                    reservation.Quantity, reservation.Id, request.ActorUserId, request.CorrelationId, utcNow);
            store.AddMovement(movement);
        }
    }

    private static HashSet<Guid> UniqueReferenceIds(IReadOnlyList<Guid> referenceIds)
    {
        var requested = referenceIds.ToHashSet();
        if (requested.Count != referenceIds.Count)
        {
            throw new DomainException(
                InventoryReservationErrors.ReferenceDuplicate,
                "A reservation request cannot contain the same reference twice.");
        }

        return requested;
    }

    private static void EnsureExactReservationSet(
        HashSet<Guid> requested,
        IReadOnlyList<InventoryReservation> reservations)
    {
        var loaded = reservations.Select(r => r.ReferenceId).ToHashSet();
        if (loaded.Count != reservations.Count || !requested.SetEquals(loaded))
        {
            throw new DomainException(
                InventoryReservationErrors.IncompleteSet,
                "The reservation set is incomplete or does not match the requested references.");
        }
    }

    private static Dictionary<Guid, InventoryItem> EnsureInventoryItems(
        IReadOnlyList<InventoryReservation> reservations,
        IReadOnlyList<InventoryItem> items)
    {
        if (items.Select(i => i.Id).Distinct().Count() != items.Count)
        {
            throw new DomainException(
                InventoryReservationErrors.IntegrityError,
                "The loaded inventory item set is inconsistent.");
        }

        var itemsById = items.ToDictionary(i => i.Id);
        foreach (var reservation in reservations)
        {
            if (!itemsById.TryGetValue(reservation.InventoryItemId, out var item))
            {
                throw new DomainException(
                    InventoryReservationErrors.IntegrityError,
                    "The reserved inventory item is missing.");
            }

            if (item.TenantId != reservation.TenantId
                || item.StoreId != reservation.StoreId
                || item.GlobalProductId != reservation.GlobalProductId)
            {
                throw new DomainException(
                    InventoryReservationErrors.IntegrityError,
                    "The reservation does not match its inventory item.");
            }
        }

        return itemsById;
    }

    private static void EnsureCompatibleStates(IReadOnlyList<InventoryReservation> reservations, bool commit)
    {
        var incompatible = commit
            ? reservations.Select(r => r.Status).Any(status =>
                status is not (InventoryReservationStatus.Active or InventoryReservationStatus.Committed))
            : reservations.Select(r => r.Status).Any(status =>
                status is not (InventoryReservationStatus.Active or InventoryReservationStatus.Released));
        if (incompatible)
        {
            throw new DomainException(
                InventoryReservationErrors.InvalidState,
                commit
                    ? "A released reservation cannot be committed as part of this batch."
                    : "A committed reservation cannot be released as part of this batch.");
        }
    }

    private sealed record StagedWrite(
        InventoryItem Item,
        InventoryMovement Movement,
        InventoryReservation Reservation);
}
