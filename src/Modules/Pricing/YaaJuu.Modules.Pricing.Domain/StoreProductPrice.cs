using YaaJuu.SharedKernel.Domain;
using YaaJuu.SharedKernel.Domain.ValueObjects;

namespace YaaJuu.Modules.Pricing.Domain;

/// <summary>
/// Versioned store override price. Current row has ValidTo = null.
/// </summary>
public sealed class StoreProductPrice : AggregateRoot
{
    private StoreProductPrice()
    {
        Currency = null!;
    }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid GlobalProductId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public DateTimeOffset ValidFrom { get; private set; }

    public DateTimeOffset? ValidTo { get; private set; }

    public bool IsCurrent => ValidTo is null;

    public Money AsMoney() => Money.FromPersistence(Amount, Currency);

    public static StoreProductPrice Open(
        Guid tenantId,
        Guid storeId,
        Guid globalProductId,
        Money money,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(money);
        if (tenantId == Guid.Empty)
        {
            throw new DomainException("store_price.tenant.required", "Store price must belong to a tenant.");
        }

        if (storeId == Guid.Empty)
        {
            throw new DomainException("store_price.store.required", "Store price must belong to a store.");
        }

        if (globalProductId == Guid.Empty)
        {
            throw new DomainException("store_price.product.required", "Global product is required.");
        }

        return new StoreProductPrice
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            StoreId = storeId,
            GlobalProductId = globalProductId,
            Amount = money.Amount,
            Currency = money.Currency,
            ValidFrom = utcNow,
            ValidTo = null,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public void Close(DateTimeOffset utcNow)
    {
        if (ValidTo is not null)
        {
            throw new DomainException("price.already_closed", "This price version is already closed.");
        }

        ValidTo = utcNow;
        UpdatedAt = utcNow;
    }
}
