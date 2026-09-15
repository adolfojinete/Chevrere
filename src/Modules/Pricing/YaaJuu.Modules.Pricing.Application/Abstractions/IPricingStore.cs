using YaaJuu.Modules.Pricing.Domain;

namespace YaaJuu.Modules.Pricing.Application.Abstractions;

public interface IPricingCatalogAccess
{
    Task<bool> GlobalProductExistsAsync(Guid globalProductId, CancellationToken cancellationToken);
}

public interface IPricingStoreAccess
{
    Task<Guid?> GetStoreTenantIdAsync(Guid storeId, CancellationToken cancellationToken);

    Task<bool> StoreProductExistsAsync(
        Guid tenantId,
        Guid storeId,
        Guid globalProductId,
        CancellationToken cancellationToken);
}

public interface IPricingStore
{
    Task<GlobalProductPrice?> GetCurrentGlobalPriceAsync(Guid globalProductId, CancellationToken cancellationToken);

    void AddGlobalPrice(GlobalProductPrice price);

    Task<(IReadOnlyList<GlobalProductPrice> Items, int Total)> ListGlobalPriceHistoryAsync(
        Guid globalProductId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<StoreProductPrice?> GetCurrentStorePriceAsync(
        Guid storeId,
        Guid globalProductId,
        CancellationToken cancellationToken);

    void AddStorePrice(StoreProductPrice price);

    Task<(IReadOnlyList<StoreProductPrice> Items, int Total)> ListStorePriceHistoryAsync(
        Guid storeId,
        Guid globalProductId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StorePriceProjection>> ListStorePricesAsync(
        Guid storeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<int> CountStoreProductsAsync(Guid storeId, CancellationToken cancellationToken);
}

public sealed record StorePriceProjection(
    Guid StoreId,
    Guid GlobalProductId,
    string Sku,
    string Name,
    decimal? SuggestedAmount,
    string? SuggestedCurrency,
    decimal? OverrideAmount,
    string? OverrideCurrency);
