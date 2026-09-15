using YaaJuu.Modules.Orders.Domain;
using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.UnitTests.Orders;

public sealed class CartDomainTests
{
    private static readonly Guid ConsumerId = Guid.CreateVersion7();
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid ProductA = Guid.CreateVersion7();
    private static readonly Guid ProductB = Guid.CreateVersion7();

    [Fact]
    public void Set_remove_clear_and_convert_follow_the_cart_contract()
    {
        var cart = Cart.Start(ConsumerId, TenantId, StoreId, FixedClock.Now);

        Assert.True(cart.SetItem(ProductA, 2, FixedClock.Now));
        Assert.False(cart.SetItem(ProductA, 2, FixedClock.Now));
        Assert.True(cart.SetItem(ProductA, 3, FixedClock.Now));
        Assert.True(cart.SetItem(ProductB, 1, FixedClock.Now));
        Assert.Equal(2, cart.Items.Count);
        Assert.Single(cart.Items, i => i.GlobalProductId == ProductA && i.Quantity == 3);

        Assert.False(cart.RemoveItem(Guid.CreateVersion7(), FixedClock.Now));
        Assert.True(cart.RemoveItem(ProductB, FixedClock.Now));
        Assert.Single(cart.Items);

        Assert.True(cart.Clear(FixedClock.Now));
        Assert.False(cart.Clear(FixedClock.Now));
        Assert.Empty(cart.Items);

        cart.SetItem(ProductA, 1, FixedClock.Now);
        cart.Convert(FixedClock.Now);
        Assert.Equal(CartStatus.Converted, cart.Status);
        Assert.Equal("cart.already_converted", Assert.Throws<DomainException>(() => cart.SetItem(ProductA, 2, FixedClock.Now)).Code);
        Assert.Equal("cart.already_converted", Assert.Throws<DomainException>(() => cart.Convert(FixedClock.Now)).Code);
    }

    [Fact]
    public void RelocateEmpty_only_works_without_items()
    {
        var cart = Cart.Start(ConsumerId, TenantId, StoreId, FixedClock.Now);
        var otherTenant = Guid.CreateVersion7();
        var otherStore = Guid.CreateVersion7();
        cart.RelocateEmpty(otherTenant, otherStore, FixedClock.Now);
        Assert.Equal(otherStore, cart.StoreId);

        cart.SetItem(ProductA, 1, FixedClock.Now);
        Assert.Equal(
            "cart.fulfillment_changed",
            Assert.Throws<DomainException>(() => cart.RelocateEmpty(TenantId, StoreId, FixedClock.Now)).Code);
    }
}
