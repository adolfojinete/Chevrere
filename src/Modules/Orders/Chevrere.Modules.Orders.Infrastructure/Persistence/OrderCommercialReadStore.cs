using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Catalog.Domain;
using Chevrere.Modules.Orders.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Chevrere.Modules.Orders.Infrastructure.Persistence;

public sealed class OrderCommercialReadStore(ChevrereDbContext dbContext) : IOrderCommercialReadStore
{
    public async Task<IReadOnlyList<OrderCommercialProduct>> GetProductsAsync(
        Guid tenantId,
        Guid storeId,
        IReadOnlyList<Guid> globalProductIds,
        CancellationToken cancellationToken)
    {
        if (globalProductIds.Count == 0)
        {
            return [];
        }

        var currentStorePrices = dbContext.StoreProductPrices.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.StoreId == storeId && p.ValidTo == null);

        var currentGlobalPrices = dbContext.GlobalProductPrices.AsNoTracking()
            .Where(p => p.ValidTo == null);

        return await (
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
                && globalProductIds.Contains(product.Id)
            select new OrderCommercialProduct(
                product.Id,
                product.Sku,
                product.Name,
                product.Brand,
                product.Presentation,
                storePrice != null ? storePrice.Amount : globalPrice!.Amount,
                storePrice != null ? storePrice.Currency : globalPrice!.Currency,
                inventory.OnHand - inventory.Reserved)
        ).ToListAsync(cancellationToken);
    }
}
