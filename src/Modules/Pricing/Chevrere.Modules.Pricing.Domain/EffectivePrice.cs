using Chevrere.Modules.Pricing.Domain.ValueObjects;

namespace Chevrere.Modules.Pricing.Domain;

public static class EffectivePrice
{
    public static (Money? Price, PriceSource Source) Resolve(Money? suggested, Money? storeOverride)
    {
        if (storeOverride is not null)
        {
            return (storeOverride, PriceSource.Store);
        }

        if (suggested is not null)
        {
            return (suggested, PriceSource.Global);
        }

        return (null, PriceSource.None);
    }
}
