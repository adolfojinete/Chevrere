using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Pricing.Application.Abstractions;
using Chevrere.Modules.Pricing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Chevrere.Modules.Pricing.Infrastructure.Persistence;

public sealed class PricingCatalogAccess(ChevrereDbContext dbContext) : IPricingCatalogAccess
{
    public Task<bool> GlobalProductExistsAsync(Guid globalProductId, CancellationToken cancellationToken) =>
        dbContext.GlobalProducts.AsNoTracking().AnyAsync(p => p.Id == globalProductId, cancellationToken);
}

public sealed class PricingStoreAccess(ChevrereDbContext dbContext) : IPricingStoreAccess
{
    public async Task<Guid?> GetStoreTenantIdAsync(Guid storeId, CancellationToken cancellationToken)
    {
        return await dbContext.Stores.AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => (Guid?)s.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> StoreProductExistsAsync(
        Guid tenantId,
        Guid storeId,
        Guid globalProductId,
        CancellationToken cancellationToken) =>
        dbContext.StoreProducts.AsNoTracking().AnyAsync(
            s => s.TenantId == tenantId && s.StoreId == storeId && s.GlobalProductId == globalProductId,
            cancellationToken);
}

public sealed class PricingStore(ChevrereDbContext dbContext) : IPricingStore
{
    public Task<GlobalProductPrice?> GetCurrentGlobalPriceAsync(
        Guid globalProductId,
        CancellationToken cancellationToken) =>
        dbContext.GlobalProductPrices
            .FirstOrDefaultAsync(p => p.GlobalProductId == globalProductId && p.ValidTo == null, cancellationToken);

    public void AddGlobalPrice(GlobalProductPrice price) => dbContext.GlobalProductPrices.Add(price);

    public async Task<(IReadOnlyList<GlobalProductPrice> Items, int Total)> ListGlobalPriceHistoryAsync(
        Guid globalProductId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.GlobalProductPrices.AsNoTracking()
            .Where(p => p.GlobalProductId == globalProductId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(p => p.ValidFrom)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<StoreProductPrice?> GetCurrentStorePriceAsync(
        Guid storeId,
        Guid globalProductId,
        CancellationToken cancellationToken) =>
        dbContext.StoreProductPrices
            .FirstOrDefaultAsync(
                p => p.StoreId == storeId && p.GlobalProductId == globalProductId && p.ValidTo == null,
                cancellationToken);

    public void AddStorePrice(StoreProductPrice price) => dbContext.StoreProductPrices.Add(price);

    public async Task<(IReadOnlyList<StoreProductPrice> Items, int Total)> ListStorePriceHistoryAsync(
        Guid storeId,
        Guid globalProductId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.StoreProductPrices.AsNoTracking()
            .Where(p => p.StoreId == storeId && p.GlobalProductId == globalProductId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(p => p.ValidFrom)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<StorePriceProjection>> ListStorePricesAsync(
        Guid storeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query =
            from offering in dbContext.StoreProducts.AsNoTracking()
            join product in dbContext.GlobalProducts.AsNoTracking() on offering.GlobalProductId equals product.Id
            where offering.StoreId == storeId
            orderby product.Name
            select new
            {
                offering.StoreId,
                offering.GlobalProductId,
                product.Sku,
                product.Name
            };

        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var productIds = rows.Select(r => r.GlobalProductId).ToList();
        var suggested = await dbContext.GlobalProductPrices.AsNoTracking()
            .Where(p => productIds.Contains(p.GlobalProductId) && p.ValidTo == null)
            .ToDictionaryAsync(p => p.GlobalProductId, cancellationToken);
        var overrides = await dbContext.StoreProductPrices.AsNoTracking()
            .Where(p => p.StoreId == storeId && productIds.Contains(p.GlobalProductId) && p.ValidTo == null)
            .ToDictionaryAsync(p => p.GlobalProductId, cancellationToken);

        return rows.Select(r =>
        {
            suggested.TryGetValue(r.GlobalProductId, out var g);
            overrides.TryGetValue(r.GlobalProductId, out var o);
            return new StorePriceProjection(
                r.StoreId,
                r.GlobalProductId,
                r.Sku,
                r.Name,
                g?.Amount,
                g?.Currency,
                o?.Amount,
                o?.Currency);
        }).ToList();
    }

    public Task<int> CountStoreProductsAsync(Guid storeId, CancellationToken cancellationToken) =>
        dbContext.StoreProducts.AsNoTracking().CountAsync(s => s.StoreId == storeId, cancellationToken);
}
