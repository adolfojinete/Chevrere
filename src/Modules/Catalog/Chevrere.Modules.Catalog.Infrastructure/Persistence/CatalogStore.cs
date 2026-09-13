using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Catalog.Application.Abstractions;
using Chevrere.Modules.Catalog.Application.Contracts;
using Chevrere.Modules.Catalog.Domain;
using Chevrere.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Chevrere.Modules.Catalog.Infrastructure.Persistence;

public sealed class CatalogStore(ChevrereDbContext dbContext) : ICatalogStore
{
    public Task<bool> CategoryCodeExistsAsync(string code, CancellationToken cancellationToken) =>
        dbContext.Categories.AnyAsync(c => c.Code == code, cancellationToken);

    public Task<bool> CategorySlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        dbContext.Categories.AnyAsync(c => c.Slug == slug, cancellationToken);

    public Task<bool> ProductSkuExistsAsync(string sku, CancellationToken cancellationToken) =>
        dbContext.GlobalProducts.AnyAsync(p => p.Sku == sku, cancellationToken);

    public Task<bool> ProductSlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        dbContext.GlobalProducts.AnyAsync(p => p.Slug == slug, cancellationToken);

    public Task<bool> ProductBarcodeExistsAsync(string barcode, Guid? exceptProductId, CancellationToken cancellationToken) =>
        dbContext.GlobalProducts.AnyAsync(
            p => p.Barcode == barcode && (exceptProductId == null || p.Id != exceptProductId),
            cancellationToken);

    public void AddCategory(Category category) => dbContext.Categories.Add(category);

    public void AddProduct(GlobalProduct product) => dbContext.GlobalProducts.Add(product);

    public void AddStoreProduct(StoreProduct storeProduct) => dbContext.StoreProducts.Add(storeProduct);

    public Task<Category?> GetCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<GlobalProduct?> GetProductAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.GlobalProducts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<StoreProduct?> GetStoreProductAsync(Guid storeId, Guid globalProductId, CancellationToken cancellationToken) =>
        dbContext.StoreProducts.FirstOrDefaultAsync(
            s => s.StoreId == storeId && s.GlobalProductId == globalProductId,
            cancellationToken);

    public Task<CategoryDto?> GetCategoryDtoAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Categories.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CategoryDto(c.Id, c.Code, c.Name, c.Slug, c.Description, c.Status, c.SortOrder, c.CreatedAt, c.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<GlobalProductDto?> GetProductDtoAsync(Guid id, CancellationToken cancellationToken) =>
        (
            from product in dbContext.GlobalProducts.AsNoTracking()
            join category in dbContext.Categories.AsNoTracking() on product.CategoryId equals category.Id
            where product.Id == id
            select new GlobalProductDto(
                product.Id,
                product.CategoryId,
                category.Name,
                product.Sku,
                product.Name,
                product.Slug,
                product.Brand,
                product.Presentation,
                product.Description,
                product.Barcode,
                product.Status,
                product.CreatedAt,
                product.UpdatedAt)
        ).FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<CategoryDto>> ListCategoriesAsync(
        CategoryListQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var source = dbContext.Categories.AsNoTracking().AsQueryable();
        if (query.Status is { } status)
        {
            source = source.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(c =>
                EF.Functions.ILike(c.Name, $"%{term}%") ||
                EF.Functions.ILike(c.Code, $"%{term}%"));
        }

        var total = await source.CountAsync(cancellationToken);
        var items = await source
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CategoryDto(c.Id, c.Code, c.Name, c.Slug, c.Description, c.Status, c.SortOrder, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<CategoryDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<GlobalProductDto>> ListProductsAsync(
        GlobalProductListQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var products = dbContext.GlobalProducts.AsNoTracking().AsQueryable();
        if (query.Status is { } status)
        {
            products = products.Where(p => p.Status == status);
        }

        if (query.CategoryId is Guid categoryId)
        {
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            products = products.Where(p =>
                EF.Functions.ILike(p.Name, $"%{term}%") ||
                EF.Functions.ILike(p.Sku, $"%{term}%") ||
                EF.Functions.ILike(p.Brand, $"%{term}%"));
        }

        products = query.SortBy?.ToLowerInvariant() switch
        {
            "sku" => products.OrderBy(p => p.Sku),
            "createdat" => products.OrderByDescending(p => p.CreatedAt),
            _ => products.OrderBy(p => p.Name)
        };

        var total = await products.CountAsync(cancellationToken);
        var items = await (
            from product in products
            join category in dbContext.Categories.AsNoTracking() on product.CategoryId equals category.Id
            select new GlobalProductDto(
                product.Id,
                product.CategoryId,
                category.Name,
                product.Sku,
                product.Name,
                product.Slug,
                product.Brand,
                product.Presentation,
                product.Description,
                product.Barcode,
                product.Status,
                product.CreatedAt,
                product.UpdatedAt)
        )
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<GlobalProductDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<CatalogProductDto>> BrowseAvailableProductsAsync(
        BrowseCatalogQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var products = dbContext.GlobalProducts.AsNoTracking()
            .Where(p => p.Status == GlobalProductStatus.Active);

        if (query.CategoryId is Guid categoryId)
        {
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            products = products.Where(p =>
                EF.Functions.ILike(p.Name, $"%{term}%") ||
                EF.Functions.ILike(p.Sku, $"%{term}%") ||
                EF.Functions.ILike(p.Brand, $"%{term}%"));
        }

        var available =
            from product in products
            join category in dbContext.Categories.AsNoTracking() on product.CategoryId equals category.Id
            where category.Status == CategoryStatus.Active
            select new { product, category };

        var total = await available.CountAsync(cancellationToken);
        var ordered = query.SortBy?.ToLowerInvariant() switch
        {
            "sku" => available.OrderBy(x => x.product.Sku),
            "createdat" => available.OrderByDescending(x => x.product.CreatedAt),
            _ => available.OrderBy(x => x.product.Name)
        };

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new CatalogProductDto(
                x.product.Id,
                x.product.CategoryId,
                x.category.Name,
                x.product.Sku,
                x.product.Name,
                x.product.Brand,
                x.product.Presentation,
                x.product.Description))
            .ToListAsync(cancellationToken);

        return new PagedResult<CatalogProductDto>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<StoreProductDto>> ListStoreProductsAsync(
        Guid storeId,
        CancellationToken cancellationToken)
    {
        return await (
            from offering in dbContext.StoreProducts.AsNoTracking()
            join product in dbContext.GlobalProducts.AsNoTracking() on offering.GlobalProductId equals product.Id
            join category in dbContext.Categories.AsNoTracking() on product.CategoryId equals category.Id
            where offering.StoreId == storeId
            orderby product.Name
            select new StoreProductDto(
                offering.Id,
                offering.StoreId,
                offering.GlobalProductId,
                product.Sku,
                product.Name,
                product.Brand,
                product.Presentation,
                category.Name,
                offering.IsEnabled,
                offering.IsEnabled && product.Status == GlobalProductStatus.Active && category.Status == CategoryStatus.Active,
                offering.UpdatedAt)
        ).ToListAsync(cancellationToken);
    }
}
