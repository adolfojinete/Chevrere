using Chevrere.Modules.Orders.Domain;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Domain.ValueObjects;

namespace Chevrere.UnitTests.Orders;

public sealed class OrderDomainTests
{
    private static readonly Guid ConsumerId = Guid.CreateVersion7();
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid CartId = Guid.CreateVersion7();
    private static readonly Guid ProductA = Guid.CreateVersion7();
    private static readonly Guid ProductB = Guid.CreateVersion7();

    [Fact]
    public void Place_snapshots_totals_and_assigns_item_ids_before_persist()
    {
        var order = Place(
            new OrderLineSnapshot(ProductA, "SKU-A", "Agua", "Cristal", "600 ml", 2, Money.Create(2500, "COP")),
            new OrderLineSnapshot(ProductB, "SKU-B", "Cola", "Coca", "350 ml", 1, Money.Create(3000, "COP")));

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(8000m, order.SubtotalAmount);
        Assert.Equal(8000m, order.TotalAmount);
        Assert.Equal("COP", order.Currency);
        Assert.Equal(FixedClock.Now.AddMinutes(15), order.ExpiresAt);
        Assert.Equal(2, order.Items.Count);
        Assert.All(order.Items, i => Assert.NotEqual(Guid.Empty, i.Id));
        Assert.Equal(5000m, order.Items.Single(i => i.GlobalProductId == ProductA).LineTotalAmount);
    }

    [Fact]
    public void Confirm_does_not_change_inventory_and_is_idempotent()
    {
        var order = Place(Line(ProductA, 2, 2500m));
        Assert.True(order.Confirm(FixedClock.Now));
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.False(order.Confirm(FixedClock.Now));
        Assert.Equal("order.cannot_cancel", Assert.Throws<DomainException>(() => order.Cancel(null, FixedClock.Now)).Code);
    }

    [Fact]
    public void Cancel_and_expire_are_idempotent_and_reject_the_other_terminal_state()
    {
        var cancellable = Place(Line(ProductA, 1, 2500m));
        Assert.True(cancellable.Cancel("changed mind", FixedClock.Now));
        Assert.False(cancellable.Cancel("again", FixedClock.Now));
        Assert.Equal("order.cannot_expire", Assert.Throws<DomainException>(() => cancellable.Expire(FixedClock.Now)).Code);

        var expirable = Place(Line(ProductA, 1, 2500m));
        Assert.True(expirable.Expire(FixedClock.Now));
        Assert.False(expirable.Expire(FixedClock.Now));
        Assert.Equal("order.cannot_cancel", Assert.Throws<DomainException>(() => expirable.Cancel(null, FixedClock.Now)).Code);
        Assert.Equal("order.cannot_confirm", Assert.Throws<DomainException>(() => expirable.Confirm(FixedClock.Now)).Code);
    }

    [Fact]
    public void Place_rejects_empty_duplicate_or_mixed_currency_lines()
    {
        Assert.Equal("order.items.required", Assert.Throws<DomainException>(() => Place()).Code);
        Assert.Equal(
            "order.item.duplicate",
            Assert.Throws<DomainException>(() => Place(Line(ProductA, 1, 2500m), Line(ProductA, 2, 2500m))).Code);
        Assert.Equal(
            "order.currency.unsupported",
            Assert.Throws<DomainException>(() => Place(
                new OrderLineSnapshot(ProductA, "SKU", "Name", "Brand", "1 L", 1, Money.FromPersistence(1, "USD")))).Code);
        Assert.Equal(
            "order.currency.mixed",
            Assert.Throws<DomainException>(() => Place(
                Line(ProductA, 1, 2500m),
                new OrderLineSnapshot(ProductB, "SKU", "Name", "Brand", "1 L", 1, Money.FromPersistence(1, "USD")))).Code);
    }

    private static Order Place(params OrderLineSnapshot[] lines) =>
        Order.Place(
            "ORD-00000001",
            ConsumerId,
            TenantId,
            StoreId,
            CartId,
            lines,
            TimeSpan.FromMinutes(15),
            FixedClock.Now);

    private static OrderLineSnapshot Line(Guid productId, long quantity, decimal amount) =>
        new(productId, "SKU", "Name", "Brand", "1 L", quantity, Money.Create(amount, "COP"));
}
