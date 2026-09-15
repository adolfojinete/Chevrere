using YaaJuu.Modules.Consumer.Application.Contracts;
using YaaJuu.Modules.Consumer.Domain;
using YaaJuu.SharedKernel.Discovery;

namespace YaaJuu.Modules.Consumer.Application.Abstractions;

/// <summary>
/// Commercially visible catalog of one resolved store. Composition of Catalog, Pricing and Inventory
/// happens in Infrastructure so Consumer.Application keeps no reference to those modules.
/// </summary>
public interface IConsumerCatalogReadStore
{
    Task<(IReadOnlyList<ConsumerProductListItemDto> Items, int TotalCount)> SearchProductsAsync(
        ConsumerFulfillmentStore store,
        string? search,
        Guid? categoryId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConsumerCategoryDto>> ListCategoriesAsync(
        ConsumerFulfillmentStore store,
        CancellationToken cancellationToken);

    Task<ConsumerProductDetailDto?> GetProductAsync(
        ConsumerFulfillmentStore store,
        Guid globalProductId,
        CancellationToken cancellationToken);
}

public interface IStoreServiceAreaStore
{
    Task<StoreServiceArea?> GetByStoreAsync(Guid storeId, CancellationToken cancellationToken);

    /// <summary>
    /// Stages the aggregate and projects its center onto the PostGIS column. Called after a create or
    /// a real reconfiguration; a no-op Configure must not call it.
    /// </summary>
    void Upsert(StoreServiceArea area);
}

/// <summary>
/// Read-only check about a Store, owned by Tenancy. Keeps Consumer free of a reference to that module.
/// </summary>
public interface IConsumerStoreAccess
{
    Task<Guid?> GetStoreTenantIdAsync(Guid storeId, CancellationToken cancellationToken);
}
