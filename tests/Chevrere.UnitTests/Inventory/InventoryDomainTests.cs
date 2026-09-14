using System.Reflection;
using Chevrere.Modules.Inventory.Domain;
using Chevrere.SharedKernel.Domain;

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

        Assert.Equal("inventory.insufficient_stock", ex.Code);
        Assert.Equal(5, item.OnHand);
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

    private static void SetPrivate(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(target, value);
    }
}
