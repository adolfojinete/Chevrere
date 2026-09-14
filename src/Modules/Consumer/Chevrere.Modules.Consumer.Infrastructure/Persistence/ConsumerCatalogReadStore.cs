using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Catalog.Domain;
using Chevrere.Modules.Consumer.Application.Abstractions;
using Chevrere.Modules.Consumer.Application.Contracts;
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
        var query = CandidateQuery(store, searchTerm, categoryId);

        var total = await query.CountAsync(cancellationToken);
        var pageRows = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        if (pageRows.Count == 0)
        {
            return ([], total);
        }

        var productIds = pageRows.Select(x => x.Id).ToList();
        var prices = await LoadEffectivePricesAsync(store, productIds, cancellationToken);

        var items = pageRows
            .Select(x =>
            {
                var (amount, currency) = prices[x.Id];
                return new ConsumerProductListItemDto(
                    x.Id,
                    x.Sku,
                    x.Name,
                    x.Brand,
                    x.Presentation,
                    x.Description,
                    x.CategoryId,
                    x.CategoryName,
                    amount,
                    currency);
            })
            .ToList();

        return (items, total);
    }

    public async Task<IReadOnlyList<ConsumerCategoryDto>> ListCategoriesAsync(
        ConsumerFulfillmentStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        var rows = await CandidateQuery(store, searchTerm: null, categoryId: null)
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

        var row = await CandidateQuery(store, searchTerm: null, categoryId: null)
            .Where(x => x.Id == globalProductId)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var prices = await LoadEffectivePricesAsync(store, [row.Id], cancellationToken);
        var (amount, currency) = prices[row.Id];
        return new ConsumerProductDetailDto(
            row.Id,
            row.Sku,
            row.Name,
            row.Brand,
            row.Presentation,
            row.Description,
            row.CategoryId,
            row.CategoryName,
            amount,
            currency);
    }

    /// <summary>
    /// Commercial gates on entity columns (so ILIKE/category filters translate), then a flat
    /// projection. Prices load in a second batched round-trip for the page only.
    /// </summary>
    private IQueryable<CandidateRow> CandidateQuery(
        ConsumerFulfillmentStore store,
        string? searchTerm,
        Guid? categoryId)
    {
        var tenantId = store.TenantId;
        var storeId = store.StoreId;

        var products =
            from offering in dbContext.StoreProducts.AsNoTracking().IgnoreQueryFilters()
            join product in dbContext.GlobalProducts.AsNoTracking() on offering.GlobalProductId equals product.Id
            join category in dbContext.Categories.AsNoTracking() on product.CategoryId equals category.Id
            join inventory in dbContext.InventoryItems.AsNoTracking().IgnoreQueryFilters()
                on new { offering.TenantId, offering.StoreId, offering.GlobalProductId }
                equals new { inventory.TenantId, inventory.StoreId, inventory.GlobalProductId }
            where offering.TenantId == tenantId
                && offering.StoreId == storeId
                && offering.IsEnabled
                && product.Status == GlobalProductStatus.Active
                && category.Status == CategoryStatus.Active
                && inventory.OnHand - inventory.Reserved > 0
                && (dbContext.StoreProductPrices.IgnoreQueryFilters().Any(p =>
                        p.TenantId == tenantId
                        && p.StoreId == storeId
                        && p.GlobalProductId == product.Id
                        && p.ValidTo == null)
                    || dbContext.GlobalProductPrices.Any(p =>
                        p.GlobalProductId == product.Id && p.ValidTo == null))
                && (categoryId == null || category.Id == categoryId)
                && (searchTerm == null
                    || EF.Functions.ILike(product.Name, searchTerm)
                    || EF.Functions.ILike(product.Sku, searchTerm)
                    || EF.Functions.ILike(product.Brand, searchTerm))
            select new CandidateRow
            {
                Id = product.Id,
                Sku = product.Sku,
                Name = product.Name,
                Brand = product.Brand,
                Presentation = product.Presentation,
                Description = product.Description,
                CategoryId = category.Id,
                CategoryName = category.Name,
                CategorySlug = category.Slug
            };

        return products;
    }

    private async Task<Dictionary<Guid, (decimal Amount, string Currency)>> LoadEffectivePricesAsync(
        ConsumerFulfillmentStore store,
        IReadOnlyList<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var overrides = await dbContext.StoreProductPrices.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.TenantId == store.TenantId
                && p.StoreId == store.StoreId
                && productIds.Contains(p.GlobalProductId)
                && p.ValidTo == null)
            .Select(p => new { p.GlobalProductId, p.Amount, p.Currency })
            .ToListAsync(cancellationToken);

        var suggested = await dbContext.GlobalProductPrices.AsNoTracking()
            .Where(p => productIds.Contains(p.GlobalProductId) && p.ValidTo == null)
            .Select(p => new { p.GlobalProductId, p.Amount, p.Currency })
            .ToListAsync(cancellationToken);

        var overrideByProduct = overrides.ToDictionary(x => x.GlobalProductId);
        var suggestedByProduct = suggested.ToDictionary(x => x.GlobalProductId);

        var result = new Dictionary<Guid, (decimal, string)>(productIds.Count);
        foreach (var id in productIds)
        {
            if (overrideByProduct.TryGetValue(id, out var storePrice))
            {
                result[id] = (storePrice.Amount, storePrice.Currency);
            }
            else if (suggestedByProduct.TryGetValue(id, out var globalPrice))
            {
                result[id] = (globalPrice.Amount, globalPrice.Currency);
            }
            else
            {
                result[id] = (0m, string.Empty);
            }
        }

        return result;
    }

    private sealed class CandidateRow
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
    }
}
