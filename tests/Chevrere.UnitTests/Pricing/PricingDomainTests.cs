using Chevrere.Modules.Pricing.Domain;
using Chevrere.Modules.Pricing.Domain.ValueObjects;
using Chevrere.SharedKernel.Domain;

namespace Chevrere.UnitTests.Pricing;

public sealed class PricingDomainTests
{
    [Fact]
    public void Money_rejects_non_positive_and_unsupported_currency()
    {
        Assert.Throws<DomainException>(() => Money.Create(0m, CurrencyCodes.Cop));
        Assert.Throws<DomainException>(() => Money.Create(-1m, CurrencyCodes.Cop));
        Assert.Throws<DomainException>(() => Money.Create(10m, "USD"));
        Assert.Throws<DomainException>(() => Money.Create(10.123m, CurrencyCodes.Cop));
    }

    [Fact]
    public void Money_equality_requires_amount_and_currency()
    {
        var a = Money.Create(6000m, "cop");
        var b = Money.Create(6000m, CurrencyCodes.Cop);
        var c = Money.Create(6500m, CurrencyCodes.Cop);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Effective_price_prefers_store_override_then_global()
    {
        var suggested = Money.Create(6000m, CurrencyCodes.Cop);
        var store = Money.Create(6500m, CurrencyCodes.Cop);

        var (withStore, sourceStore) = EffectivePrice.Resolve(suggested, store);
        Assert.Equal(store, withStore);
        Assert.Equal(PriceSource.Store, sourceStore);

        var (withGlobal, sourceGlobal) = EffectivePrice.Resolve(suggested, null);
        Assert.Equal(suggested, withGlobal);
        Assert.Equal(PriceSource.Global, sourceGlobal);

        var (none, sourceNone) = EffectivePrice.Resolve(null, null);
        Assert.Null(none);
        Assert.Equal(PriceSource.None, sourceNone);

        var (storeOnly, sourceStoreOnly) = EffectivePrice.Resolve(null, store);
        Assert.Equal(store, storeOnly);
        Assert.Equal(PriceSource.Store, sourceStoreOnly);
    }

    [Fact]
    public void Global_price_close_and_reopen_preserve_history_semantics()
    {
        var first = GlobalProductPrice.Open(Guid.CreateVersion7(), Money.Create(6000m, CurrencyCodes.Cop), FixedClock.Now);
        Assert.True(first.IsCurrent);
        first.Close(FixedClock.Now.AddMinutes(1));
        Assert.False(first.IsCurrent);
        Assert.Throws<DomainException>(() => first.Close(FixedClock.Now.AddMinutes(2)));

        var second = GlobalProductPrice.Open(first.GlobalProductId, Money.Create(6500m, CurrencyCodes.Cop), FixedClock.Now.AddMinutes(2));
        Assert.True(second.IsCurrent);
        Assert.Equal(6500m, second.Amount);
    }

    [Fact]
    public void Store_price_requires_tenant_and_store()
    {
        var money = Money.Create(6200m, CurrencyCodes.Cop);
        Assert.Throws<DomainException>(() =>
            StoreProductPrice.Open(Guid.Empty, Guid.CreateVersion7(), Guid.CreateVersion7(), money, FixedClock.Now));
        Assert.Throws<DomainException>(() =>
            StoreProductPrice.Open(Guid.CreateVersion7(), Guid.Empty, Guid.CreateVersion7(), money, FixedClock.Now));
    }
}
