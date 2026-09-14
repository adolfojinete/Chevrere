using Chevrere.Modules.Inventory.Domain;
using Chevrere.SharedKernel.Domain;

namespace Chevrere.UnitTests.Inventory;

public sealed class InventoryReservationTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid ItemId = Guid.CreateVersion7();
    private static readonly Guid ProductId = Guid.CreateVersion7();
    private static readonly Guid ReferenceId = Guid.CreateVersion7();

    [Fact]
    public void Open_starts_active_and_rejects_non_positive_quantity()
    {
        var reservation = InventoryReservation.Open(
            TenantId, StoreId, ItemId, ProductId, "OrderItem", ReferenceId, 2, FixedClock.Now.AddMinutes(15), FixedClock.Now);

        Assert.Equal(InventoryReservationStatus.Active, reservation.Status);
        Assert.Equal(2, reservation.Quantity);

        Assert.Equal(
            "inventory.reservation.quantity.invalid",
            Assert.Throws<DomainException>(() => InventoryReservation.Open(
                TenantId, StoreId, ItemId, ProductId, "OrderItem", Guid.CreateVersion7(), 0, null, FixedClock.Now)).Code);
    }

    [Fact]
    public void Release_and_commit_are_idempotent_and_reject_the_opposite_terminal_state()
    {
        var reservation = InventoryReservation.Open(
            TenantId, StoreId, ItemId, ProductId, "OrderItem", ReferenceId, 1, null, FixedClock.Now);

        Assert.True(reservation.Release(FixedClock.Now));
        Assert.False(reservation.Release(FixedClock.Now));
        Assert.Equal(
            "inventory.reservation.already_released",
            Assert.Throws<DomainException>(() => reservation.Commit(FixedClock.Now)).Code);

        var other = InventoryReservation.Open(
            TenantId, StoreId, ItemId, ProductId, "OrderItem", Guid.CreateVersion7(), 1, null, FixedClock.Now);
        Assert.True(other.Commit(FixedClock.Now));
        Assert.False(other.Commit(FixedClock.Now));
        Assert.Equal(
            "inventory.reservation.already_committed",
            Assert.Throws<DomainException>(() => other.Release(FixedClock.Now)).Code);
    }
}
