using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Catalog.Domain;
using Chevrere.Modules.Consumer.Application.Abstractions;
using Chevrere.Modules.Consumer.Application.Contracts;
using Chevrere.SharedKernel.Discovery;
using Microsoft.EntityFrameworkCore;

namespace Chevrere.Modules.Consumer.Infrastructure.Persistence;

public sealed class ConsumerCatalogReadStore(ChevrereDbContext dbContext) : IConsumerCatalogReadStore
{
    public async Task<(IReadOnlyList<ConsumerProductListItemDto> Items, int TotalCount)> SearchProductsAsync(
        ConsumerFulfillmentStore store,
        string? search,
        Guid? categoryId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        var searchTerm = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        var query = CommercialProductsQuery(store, searchTerm, categoryId);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ConsumerProductListItemDto(
                x.Id,
                x.Sku,
                x.Name,
                x.Brand,
                x.Presentation,
                x.Description,
                x.CategoryId,
                x.CategoryName,
                x.EffectiveAmount,
                x.EffectiveCurrency))
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<ConsumerCategoryDto>> ListCategoriesAsync(
        ConsumerFulfillmentStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        var rows = await CommercialProductsQuery(store, searchTerm: null, categoryId: null)
            .Select(x => new { x.CategoryId, x.CategoryName, x.CategorySlug })
            .Distinct()
            .OrderBy(x => x.CategoryName)
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new ConsumerCategoryDto(x.CategoryId, x.CategoryName, x.CategorySlug))
            .ToList();
    }

    public async Task<ConsumerProductDetailDto?> GetProductAsync(
        ConsumerFulfillmentStore store,
        Guid globalProductId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        return await CommercialProductsQuery(store, searchTerm: null, categoryId: null)
            .Where(x => x.Id == globalProductId)
            .Select(x => new ConsumerProductDetailDto(
                x.Id,
                x.Sku,
                x.Name,
                x.Brand,
                x.Presentation,
                x.Description,
                x.CategoryId,
                x.CategoryName,
                x.EffectiveAmount,
                x.EffectiveCurrency))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Commercial query resolves current effective price and stock visibility server-side.
    /// Count, page, categories and product detail all share this definition: a product is visible
    /// only when Catalog, Inventory and a current EffectivePrice (store override else global
    /// suggested) hold in the same SQL composition. There is no later price lookup.
    /// </summary>
    private IQueryable<CommercialProductRow> CommercialProductsQuery(
        ConsumerFulfillmentStore store,
        string? searchTerm,
        Guid? categoryId)
    {
        var tenantId = store.TenantId;
        var storeId = store.StoreId;

        var currentStorePrices = dbContext.StoreProductPrices.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.StoreId == storeId && p.ValidTo == null);

        var currentGlobalPrices = dbContext.GlobalProductPrices.AsNoTracking()
            .Where(p => p.ValidTo == null);

        return
            from offering in dbContext.StoreProducts.AsNoTracking().IgnoreQueryFilters()
            join product in dbContext.GlobalProducts.AsNoTracking() on offering.GlobalProductId equals product.Id
            join category in dbContext.Categories.AsNoTracking() on product.CategoryId equals category.Id
            join inventory in dbContext.InventoryItems.AsNoTracking().IgnoreQueryFilters()
                on new { offering.TenantId, offering.StoreId, offering.GlobalProductId }
                equals new { inventory.TenantId, inventory.StoreId, inventory.GlobalProductId }
            join storePrice in currentStorePrices
                on product.Id equals storePrice.GlobalProductId into storePriceGroup
            from storePrice in storePriceGroup.DefaultIfEmpty()
            join globalPrice in currentGlobalPrices
                on product.Id equals globalPrice.GlobalProductId into globalPriceGroup
            from globalPrice in globalPriceGroup.DefaultIfEmpty()
            where offering.TenantId == tenantId
                && offering.StoreId == storeId
                && offering.IsEnabled
                && product.Status == GlobalProductStatus.Active
                && category.Status == CategoryStatus.Active
                && inventory.OnHand - inventory.Reserved > 0
                && (storePrice != null || globalPrice != null)
                && (categoryId == null || category.Id == categoryId)
                && (searchTerm == null
                    || EF.Functions.ILike(product.Name, searchTerm)
                    || EF.Functions.ILike(product.Sku, searchTerm)
                    || EF.Functions.ILike(product.Brand, searchTerm))
            select new CommercialProductRow
            {
                Id = product.Id,
                Sku = product.Sku,
                Name = product.Name,
                Brand = product.Brand,
                Presentation = product.Presentation,
                Description = product.Description,
                CategoryId = category.Id,
                CategoryName = category.Name,
                CategorySlug = category.Slug,
                EffectiveAmount = storePrice != null ? storePrice.Amount : globalPrice!.Amount,
                EffectiveCurrency = storePrice != null ? storePrice.Currency : globalPrice!.Currency
            };
    }

    private sealed class CommercialProductRow
    {
        public Guid Id { get; init; }
        public string Sku { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Brand { get; init; } = string.Empty;
        public string Presentation { get; init; } = string.Empty;
        public string? Description { get; init; }
        public Guid CategoryId { get; init; }
        public string CategoryName { get; init; } = string.Empty;
        public string? CategorySlug { get; init; }
        public decimal EffectiveAmount { get; init; }
        public string EffectiveCurrency { get; init; } = string.Empty;
    }
}
