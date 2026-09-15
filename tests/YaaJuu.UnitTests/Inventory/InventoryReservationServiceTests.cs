using System.Reflection;
using YaaJuu.Modules.Inventory.Application.Abstractions;
using YaaJuu.Modules.Inventory.Domain;
using YaaJuu.Modules.Inventory.Infrastructure.Persistence;
using YaaJuu.SharedKernel.Domain;
using YaaJuu.SharedKernel.Inventory;
using YaaJuu.SharedKernel.Time;
using NSubstitute;

namespace YaaJuu.UnitTests.Inventory;

public sealed class InventoryReservationServiceTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid ActorId = Guid.CreateVersion7();

    [Fact]
    public async Task Empty_reference_set_is_a_no_op()
    {
        var store = Substitute.For<IInventoryStore>();
        var service = Service(store);

        await service.ReleaseAsync(Request([]), CancellationToken.None);
        await service.CommitAsync(Request([]), CancellationToken.None);

        await store.DidNotReceive().GetReservationsByReferencesAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
        store.DidNotReceive().AddMovement(Arg.Any<InventoryMovement>());
    }

    [Fact]
    public async Task Duplicate_references_are_rejected_before_the_store_is_queried()
    {
        var store = Substitute.For<IInventoryStore>();
        var service = Service(store);
        var duplicate = Guid.CreateVersion7();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.ReleaseAsync(Request([duplicate, duplicate]), CancellationToken.None));

        Assert.Equal(InventoryReservationErrors.ReferenceDuplicate, ex.Code);
        await store.DidNotReceive().GetReservationsByReferencesAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
        store.DidNotReceive().AddMovement(Arg.Any<InventoryMovement>());
    }

    [Fact]
    public async Task Release_of_an_incomplete_set_mutates_nothing()
    {
        var a = Hold(2);
        var c = Hold(3);
        var missing = Guid.CreateVersion7();
        var (service, store, movements) = ServiceWith(a, c);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.ReleaseAsync(Request([a.ReferenceId, missing, c.ReferenceId]), CancellationToken.None));

        Assert.Equal(InventoryReservationErrors.IncompleteSet, ex.Code);
        AssertUnchanged(a, c);
        Assert.Empty(movements);
        await store.DidNotReceive().GetItemsByProductsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
        store.DidNotReceive().AddMovement(Arg.Any<InventoryMovement>());
    }

    [Fact]
    public async Task Commit_of_an_incomplete_set_mutates_nothing()
    {
        var a = Hold(2);
        var c = Hold(3);
        var missing = Guid.CreateVersion7();
        var (service, _, movements) = ServiceWith(a, c);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.CommitAsync(Request([a.ReferenceId, missing, c.ReferenceId]), CancellationToken.None));

        Assert.Equal(InventoryReservationErrors.IncompleteSet, ex.Code);
        AssertUnchanged(a, c);
        Assert.Equal(a.OnHand, a.Item.OnHand);
        Assert.Empty(movements);
    }

    [Fact]
    public async Task Missing_inventory_item_fails_before_any_reservation_transition()
    {
        var a = Hold(2);
        var c = Hold(1);
        var store = Substitute.For<IInventoryStore>();
        WireReservations(store, a, c);
        store.GetItemsByProductsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<InventoryItem>)[a.Item]);
        var movements = TrackMovements(store);
        var service = Service(store);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.ReleaseAsync(Request([a.ReferenceId, c.ReferenceId]), CancellationToken.None));

        Assert.Equal(InventoryReservationErrors.IntegrityError, ex.Code);
        AssertUnchanged(a, c);
        Assert.Empty(movements);
    }

    [Fact]
    public async Task Inventory_item_identity_mismatch_fails_before_mutation()
    {
        var a = Hold(2);
        var c = Hold(1);
        SetPrivate(c.Item, nameof(InventoryItem.StoreId), Guid.CreateVersion7());
        var (service, _, movements) = ServiceWith(a, c);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.ReleaseAsync(Request([a.ReferenceId, c.ReferenceId]), CancellationToken.None));

        Assert.Equal(InventoryReservationErrors.IntegrityError, ex.Code);
        Assert.Equal(InventoryReservationStatus.Active, a.Reservation.Status);
        Assert.Equal(InventoryReservationStatus.Active, c.Reservation.Status);
        Assert.Empty(movements);
    }

    [Fact]
    public async Task Release_rejects_a_committed_line_before_touching_active_siblings()
    {
        var a = Hold(2);
        var b = Hold(1);
        var c = Hold(3);
        b.Reservation.Commit(FixedClock.Now);
        b.Item.CommitReservation(b.Quantity, b.Reservation.Id, ActorId, "corr", FixedClock.Now);
        var (service, _, movements) = ServiceWith(a, b, c);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.ReleaseAsync(Request([a.ReferenceId, b.ReferenceId, c.ReferenceId]), CancellationToken.None));

        Assert.Equal(InventoryReservationErrors.InvalidState, ex.Code);
        Assert.Equal(InventoryReservationStatus.Active, a.Reservation.Status);
        Assert.Equal(InventoryReservationStatus.Committed, b.Reservation.Status);
        Assert.Equal(InventoryReservationStatus.Active, c.Reservation.Status);
        Assert.Equal(a.Reserved, a.Item.Reserved);
        Assert.Equal(c.Reserved, c.Item.Reserved);
        Assert.Empty(movements);
    }

    [Fact]
    public async Task Commit_rejects_a_released_line_before_touching_active_siblings()
    {
        var a = Hold(2);
        var b = Hold(1);
        var c = Hold(3);
        b.Reservation.Release(FixedClock.Now);
        b.Item.ReleaseReservation(b.Quantity, b.Reservation.Id, ActorId, "corr", FixedClock.Now);
        var (service, _, movements) = ServiceWith(a, b, c);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.CommitAsync(Request([a.ReferenceId, b.ReferenceId, c.ReferenceId]), CancellationToken.None));

        Assert.Equal(InventoryReservationErrors.InvalidState, ex.Code);
        Assert.Equal(InventoryReservationStatus.Active, a.Reservation.Status);
        Assert.Equal(InventoryReservationStatus.Released, b.Reservation.Status);
        Assert.Equal(InventoryReservationStatus.Active, c.Reservation.Status);
        Assert.Equal(a.OnHand, a.Item.OnHand);
        Assert.Equal(c.OnHand, c.Item.OnHand);
        Assert.Empty(movements);
    }

    [Fact]
    public async Task Release_retry_mix_is_idempotent_for_already_released_lines()
    {
        var a = Hold(2);
        var b = Hold(1);
        var c = Hold(3);
        b.Reservation.Release(FixedClock.Now);
        b.Item.ReleaseReservation(b.Quantity, b.Reservation.Id, ActorId, "corr", FixedClock.Now);
        var (service, _, movements) = ServiceWith(a, b, c);

        await service.ReleaseAsync(Request([a.ReferenceId, b.ReferenceId, c.ReferenceId]), CancellationToken.None);

        Assert.Equal(InventoryReservationStatus.Released, a.Reservation.Status);
        Assert.Equal(InventoryReservationStatus.Released, b.Reservation.Status);
        Assert.Equal(InventoryReservationStatus.Released, c.Reservation.Status);
        Assert.Equal(2, movements.Count);
        Assert.All(movements, m => Assert.Equal(InventoryMovementType.ReservationReleased, m.Type));
        Assert.DoesNotContain(movements, m => m.ReferenceId == b.Reservation.Id);
        Assert.Equal(0, a.Item.Reserved);
        Assert.Equal(0, b.Item.Reserved);
        Assert.Equal(0, c.Item.Reserved);
    }

    [Fact]
    public async Task Commit_retry_mix_is_idempotent_for_already_committed_lines()
    {
        var a = Hold(2);
        var b = Hold(1);
        var c = Hold(3);
        b.Reservation.Commit(FixedClock.Now);
        b.Item.CommitReservation(b.Quantity, b.Reservation.Id, ActorId, "corr", FixedClock.Now);
        var (service, _, movements) = ServiceWith(a, b, c);

        await service.CommitAsync(Request([a.ReferenceId, b.ReferenceId, c.ReferenceId]), CancellationToken.None);

        Assert.Equal(InventoryReservationStatus.Committed, a.Reservation.Status);
        Assert.Equal(InventoryReservationStatus.Committed, b.Reservation.Status);
        Assert.Equal(InventoryReservationStatus.Committed, c.Reservation.Status);
        Assert.Equal(2, movements.Count);
        Assert.All(movements, m => Assert.Equal(InventoryMovementType.ReservationCommitted, m.Type));
        Assert.DoesNotContain(movements, m => m.ReferenceId == b.Reservation.Id);
        Assert.Equal(a.OnHand - a.Quantity, a.Item.OnHand);
        Assert.Equal(0, a.Item.Reserved);
        Assert.Equal(0, b.Item.Reserved);
        Assert.Equal(c.OnHand - c.Quantity, c.Item.OnHand);
    }

    private static InventoryReservationService Service(IInventoryStore store)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedClock.Now);
        return new InventoryReservationService(store, clock);
    }

    private static (InventoryReservationService Service, IInventoryStore Store, List<InventoryMovement> Movements) ServiceWith(
        params HeldLine[] lines)
    {
        var store = Substitute.For<IInventoryStore>();
        WireReservations(store, lines);
        store.GetItemsByProductsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var products = call.ArgAt<IReadOnlyList<Guid>>(2);
                return (IReadOnlyList<InventoryItem>)[.. lines
                    .Select(l => l.Item)
                    .Where(i => products.Contains(i.GlobalProductId))];
            });
        var movements = TrackMovements(store);
        return (Service(store), store, movements);
    }

    private static void WireReservations(IInventoryStore store, params HeldLine[] lines)
    {
        store.GetReservationsByReferencesAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var ids = call.ArgAt<IReadOnlyList<Guid>>(3);
                return (IReadOnlyList<InventoryReservation>)[.. lines
                    .Where(l => ids.Contains(l.ReferenceId))
                    .Select(l => l.Reservation)];
            });
    }

    private static List<InventoryMovement> TrackMovements(IInventoryStore store)
    {
        var movements = new List<InventoryMovement>();
        store.When(s => s.AddMovement(Arg.Any<InventoryMovement>()))
            .Do(call => movements.Add(call.Arg<InventoryMovement>()));
        return movements;
    }

    private static InventoryReservationReleaseRequest Request(IReadOnlyList<Guid> referenceIds) =>
        new(TenantId, StoreId, ActorId, "corr", InventoryReferenceTypes.OrderItem, referenceIds);

    private static HeldLine Hold(long quantity)
    {
        var productId = Guid.CreateVersion7();
        var (item, _) = InventoryItem.Initialize(
            TenantId, StoreId, productId, 10, ActorId, "corr", FixedClock.Now);
        var referenceId = Guid.CreateVersion7();
        var reservation = InventoryReservation.Open(
            TenantId, StoreId, item.Id, productId, InventoryReferenceTypes.OrderItem, referenceId, quantity, null, FixedClock.Now);
        item.Reserve(quantity, reservation.Id, ActorId, "corr", FixedClock.Now);
        return new HeldLine(item, reservation, referenceId, quantity, item.OnHand, item.Reserved);
    }

    private static void AssertUnchanged(params HeldLine[] lines)
    {
        foreach (var line in lines)
        {
            Assert.Equal(InventoryReservationStatus.Active, line.Reservation.Status);
            Assert.Equal(line.Reserved, line.Item.Reserved);
            Assert.Equal(line.OnHand, line.Item.OnHand);
        }
    }

    private static void SetPrivate(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(target, value);
    }

    private sealed record HeldLine(
        InventoryItem Item,
        InventoryReservation Reservation,
        Guid ReferenceId,
        long Quantity,
        long OnHand,
        long Reserved);
}
