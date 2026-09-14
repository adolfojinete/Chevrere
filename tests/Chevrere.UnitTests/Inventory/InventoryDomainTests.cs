using System.Reflection;
using Chevrere.Modules.Inventory.Domain;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Inventory;

namespace Chevrere.UnitTests.Inventory;

public sealed class InventoryDomainTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid ProductId = Guid.CreateVersion7();
    private static readonly Guid ActorId = Guid.CreateVersion7();

    [Fact]
    public void Initialize_positive_creates_item_and_initial_stock_movement()
    {
        var (item, movement) = InventoryItem.Initialize(
            TenantId, StoreId, ProductId, 50, ActorId, "corr-1", FixedClock.Now);

        Assert.Equal(50, item.OnHand);
        Assert.Equal(0, item.Reserved);
        Assert.Equal(50, item.Available);
        Assert.NotNull(movement);
        Assert.Equal(InventoryMovementType.InitialStock, movement!.Type);
        Assert.Equal(50, movement.OnHandDelta);
        Assert.Equal(0, movement.ReservedDelta);
        Assert.Equal(0, movement.OnHandBefore);
        Assert.Equal(50, movement.OnHandAfter);
        Assert.Equal(0, movement.ReservedBefore);
        Assert.Equal(0, movement.ReservedAfter);
    }

    [Fact]
    public void Initialize_zero_creates_item_without_movement()
    {
        var (item, movement) = InventoryItem.Initialize(
            TenantId, StoreId, ProductId, 0, ActorId, "corr-0", FixedClock.Now);

        Assert.Equal(0, item.OnHand);
        Assert.Equal(0, item.Reserved);
        Assert.Equal(0, item.Available);
        Assert.Null(movement);
    }

    [Fact]
    public void Increase_decrease_waste_preserve_before_after_chain()
    {
        var (item, _) = InventoryItem.Initialize(
            TenantId, StoreId, ProductId, 50, ActorId, "corr", FixedClock.Now);

        var increase = item.Increase(10, "Conteo físico", ActorId, "corr", FixedClock.Now);
        Assert.Equal(50, increase.OnHandBefore);
        Assert.Equal(60, increase.OnHandAfter);
        Assert.Equal(10, increase.OnHandDelta);

        var decrease = item.Decrease(5, "Corrección inventario", ActorId, "corr", FixedClock.Now);
        Assert.Equal(60, decrease.OnHandBefore);
        Assert.Equal(55, decrease.OnHandAfter);
        Assert.Equal(-5, decrease.OnHandDelta);

        var waste = item.RecordWaste(2, "Producto roto", ActorId, "corr", FixedClock.Now);
        Assert.Equal(55, waste.OnHandBefore);
        Assert.Equal(53, waste.OnHandAfter);
        Assert.Equal(-2, waste.OnHandDelta);
        Assert.Equal(InventoryMovementType.Waste, waste.Type);

        Assert.Equal(53, item.OnHand);
        Assert.Equal(0, item.Reserved);
        Assert.Equal(53, item.Available);
    }

    [Fact]
    public void Decrease_insufficient_stock_throws_without_mutating()
    {
        var (item, _) = InventoryItem.Initialize(
            TenantId, StoreId, ProductId, 5, ActorId, "corr", FixedClock.Now);

        var ex = Assert.Throws<DomainException>(() =>
            item.Decrease(6, "Conteo", ActorId, "corr", FixedClock.Now));

        Assert.Equal("inventory.insufficient_available", ex.Code);
        Assert.Equal(5, item.OnHand);
    }

    [Fact]
    public void Waste_rejects_quantity_above_available_when_units_are_reserved()
    {
        var (item, _) = InventoryItem.Initialize(
            TenantId, StoreId, ProductId, 5, ActorId, "corr", FixedClock.Now);
        var reservationId = Guid.CreateVersion7();
        item.Reserve(3, reservationId, ActorId, "corr", FixedClock.Now);

        var ex = Assert.Throws<DomainException>(() =>
            item.RecordWaste(3, "Merma", ActorId, "corr", FixedClock.Now));

        Assert.Equal("inventory.insufficient_available", ex.Code);
        Assert.Equal(5, item.OnHand);
        Assert.Equal(3, item.Reserved);
    }

    [Fact]
    public void Reserve_release_and_commit_preserve_balances_and_are_idempotent_on_the_reservation()
    {
        var (item, _) = InventoryItem.Initialize(
            TenantId, StoreId, ProductId, 10, ActorId, "corr", FixedClock.Now);
        var reservationId = Guid.CreateVersion7();

        var hold = item.Reserve(3, reservationId, ActorId, "corr", FixedClock.Now);
        Assert.Equal(InventoryMovementType.Reservation, hold.Type);
        Assert.Equal(InventoryReferenceTypes.InventoryReservation, hold.ReferenceType);
        Assert.Equal(reservationId, hold.ReferenceId);
        Assert.Equal(10, item.OnHand);
        Assert.Equal(3, item.Reserved);
        Assert.Equal(7, item.Available);

        var released = item.ReleaseReservation(3, reservationId, ActorId, "corr", FixedClock.Now);
        Assert.Equal(InventoryMovementType.ReservationReleased, released.Type);
        Assert.Equal(10, item.OnHand);
        Assert.Equal(0, item.Reserved);

        var (item2, _) = InventoryItem.Initialize(
            TenantId, StoreId, Guid.CreateVersion7(), 10, ActorId, "corr", FixedClock.Now);
        var commitId = Guid.CreateVersion7();
        item2.Reserve(3, commitId, ActorId, "corr", FixedClock.Now);
        var committed = item2.CommitReservation(3, commitId, ActorId, "corr", FixedClock.Now);
        Assert.Equal(InventoryMovementType.ReservationCommitted, committed.Type);
        Assert.Equal(7, item2.OnHand);
        Assert.Equal(0, item2.Reserved);
        Assert.Equal(7, item2.Available);
    }

    [Fact]
    public void Reserve_rejects_when_available_is_insufficient()
    {
        var (item, _) = InventoryItem.Initialize(
            TenantId, StoreId, ProductId, 2, ActorId, "corr", FixedClock.Now);

        var ex = Assert.Throws<DomainException>(() =>
            item.Reserve(3, Guid.CreateVersion7(), ActorId, "corr", FixedClock.Now));
        Assert.Equal("inventory.insufficient_available", ex.Code);
        Assert.Equal(0, item.Reserved);
    }

    [Fact]
    public void Manual_mutations_require_reason_and_positive_quantity()
    {
        var (item, _) = InventoryItem.Initialize(
            TenantId, StoreId, ProductId, 10, ActorId, "corr", FixedClock.Now);

        Assert.Throws<DomainException>(() => item.Increase(1, " ", ActorId, "corr", FixedClock.Now));
        Assert.Throws<DomainException>(() => item.Increase(0, "ok", ActorId, "corr", FixedClock.Now));
        Assert.Throws<DomainException>(() => item.RecordWaste(-1, "roto", ActorId, "corr", FixedClock.Now));
        Assert.Throws<DomainException>(() =>
            InventoryItem.Initialize(TenantId, StoreId, ProductId, -1, ActorId, "corr", FixedClock.Now));
    }

    [Fact]
    public void Available_is_on_hand_minus_reserved()
    {
        var (item, _) = InventoryItem.Initialize(
            TenantId, StoreId, ProductId, 10, ActorId, "corr", FixedClock.Now);

        SetPrivate(item, nameof(InventoryItem.Reserved), 3L);
        Assert.Equal(10, item.OnHand);
        Assert.Equal(3, item.Reserved);
        Assert.Equal(7, item.Available);
    }

    [Fact]
    public void Overflow_on_increase_is_rejected()
    {
        var (item, _) = InventoryItem.Initialize(
            TenantId, StoreId, ProductId, long.MaxValue, ActorId, "corr", FixedClock.Now);

        Assert.ThrowsAny<OverflowException>(() =>
            item.Increase(1, "overflow", ActorId, "corr", FixedClock.Now));
        Assert.Equal(long.MaxValue, item.OnHand);
    }

    [Fact]
    public void CreateForReceipt_starts_empty_without_a_movement()
    {
        var item = InventoryItem.CreateForReceipt(TenantId, StoreId, ProductId, FixedClock.Now);

        Assert.Equal(TenantId, item.TenantId);
        Assert.Equal(StoreId, item.StoreId);
        Assert.Equal(ProductId, item.GlobalProductId);
        Assert.Equal(0, item.OnHand);
        Assert.Equal(0, item.Available);
    }

    [Fact]
    public void Receive_posts_a_receipt_movement_carrying_its_reference()
    {
        var item = InventoryItem.CreateForReceipt(TenantId, StoreId, ProductId, FixedClock.Now);
        var receiptItemId = Guid.CreateVersion7();

        var movement = item.Receive(
            25, InventoryReferenceTypes.GoodsReceiptItem, receiptItemId, ActorId, "corr-r", FixedClock.Now);

        Assert.Equal(InventoryMovementType.Receipt, movement.Type);
        Assert.Equal(InventoryReferenceTypes.GoodsReceiptItem, movement.ReferenceType);
        Assert.Equal(receiptItemId, movement.ReferenceId);
        Assert.Equal(0, movement.OnHandBefore);
        Assert.Equal(25, movement.OnHandAfter);
        Assert.Equal(25, movement.OnHandDelta);
        Assert.Equal(0, movement.ReservedDelta);
        Assert.Null(movement.Reason);
        Assert.Equal(25, item.OnHand);
        Assert.Equal(25, item.Available);
    }

    [Fact]
    public void Receive_adds_on_top_of_existing_stock_without_touching_reservations()
    {
        var (item, _) = InventoryItem.Initialize(
            TenantId, StoreId, ProductId, 40, ActorId, "corr", FixedClock.Now);
        SetPrivate(item, nameof(InventoryItem.Reserved), 10L);

        var movement = item.Receive(
            15, InventoryReferenceTypes.GoodsReceiptItem, Guid.CreateVersion7(), ActorId, "corr", FixedClock.Now);

        Assert.Equal(40, movement.OnHandBefore);
        Assert.Equal(55, movement.OnHandAfter);
        Assert.Equal(10, movement.ReservedBefore);
        Assert.Equal(10, movement.ReservedAfter);
        Assert.Equal(55, item.OnHand);
        Assert.Equal(45, item.Available);
    }

    [Fact]
    public void Receive_requires_positive_quantity_and_a_reference()
    {
        var item = InventoryItem.CreateForReceipt(TenantId, StoreId, ProductId, FixedClock.Now);
        var referenceId = Guid.CreateVersion7();

        Assert.Equal("inventory.quantity.invalid", Assert.Throws<DomainException>(() => item.Receive(
            0, InventoryReferenceTypes.GoodsReceiptItem, referenceId, ActorId, "corr", FixedClock.Now)).Code);
        Assert.Equal("inventory.quantity.invalid", Assert.Throws<DomainException>(() => item.Receive(
            -5, InventoryReferenceTypes.GoodsReceiptItem, referenceId, ActorId, "corr", FixedClock.Now)).Code);
        Assert.Equal("inventory.reference.required", Assert.Throws<DomainException>(() => item.Receive(
            5, InventoryReferenceTypes.GoodsReceiptItem, Guid.Empty, ActorId, "corr", FixedClock.Now)).Code);
        Assert.Throws<DomainException>(() => item.Receive(5, " ", referenceId, ActorId, "corr", FixedClock.Now));

        Assert.Equal(0, item.OnHand);
    }

    [Fact]
    public void Manual_movements_carry_no_reference()
    {
        var (item, initial) = InventoryItem.Initialize(
            TenantId, StoreId, ProductId, 10, ActorId, "corr", FixedClock.Now);

        Assert.Null(initial!.ReferenceType);
        Assert.Null(initial.ReferenceId);

        var increase = item.Increase(1, "Conteo", ActorId, "corr", FixedClock.Now);
        Assert.Null(increase.ReferenceType);
        Assert.Null(increase.ReferenceId);
    }

    private static void SetPrivate(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(target, value);
    }
}
