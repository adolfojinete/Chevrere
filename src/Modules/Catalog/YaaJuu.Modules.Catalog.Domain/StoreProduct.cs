using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.Modules.Catalog.Domain;

public sealed class StoreProduct : AggregateRoot
{
    private StoreProduct()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid GlobalProductId { get; private set; }

    public bool IsEnabled { get; private set; }

    public static StoreProduct EnableForStore(
        Guid tenantId,
        Guid storeId,
        GlobalProduct product,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (tenantId == Guid.Empty)
        {
            throw new DomainException("store_product.tenant.required", "StoreProduct must belong to a tenant.");
        }

        if (storeId == Guid.Empty)
        {
            throw new DomainException("store_product.store.required", "StoreProduct must belong to a store.");
        }

        product.EnsureCanBeOffered();

        return new StoreProduct
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            StoreId = storeId,
            GlobalProductId = product.Id,
            IsEnabled = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    /// <returns>true when IsEnabled actually changed to true.</returns>
    public bool Enable(GlobalProduct product, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (product.Id != GlobalProductId)
        {
            throw new DomainException("store_product.product.mismatch", "The product does not match this store offering.");
        }

        product.EnsureCanBeOffered();

        if (IsEnabled)
        {
            return false;
        }

        IsEnabled = true;
        UpdatedAt = utcNow;
        return true;
    }

    /// <returns>true when IsEnabled actually changed to false.</returns>
    public bool Disable(DateTimeOffset utcNow)
    {
        if (!IsEnabled)
        {
            return false;
        }

        IsEnabled = false;
        UpdatedAt = utcNow;
        return true;
    }

    public bool BelongsTo(Guid tenantId, Guid storeId) =>
        TenantId == tenantId && StoreId == storeId;
}
