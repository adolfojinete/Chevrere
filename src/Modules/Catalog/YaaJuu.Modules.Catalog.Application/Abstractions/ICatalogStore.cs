using YaaJuu.Modules.Catalog.Application.Contracts;
using YaaJuu.Modules.Catalog.Domain;
using YaaJuu.SharedKernel.Results;

namespace YaaJuu.Modules.Catalog.Application.Abstractions;

public interface ICatalogStore
{
    Task<bool> CategoryCodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<bool> CategorySlugExistsAsync(string slug, CancellationToken cancellationToken);

    Task<bool> ProductSkuExistsAsync(string sku, CancellationToken cancellationToken);

    Task<bool> ProductSlugExistsAsync(string slug, CancellationToken cancellationToken);

    Task<bool> ProductBarcodeExistsAsync(string barcode, Guid? exceptProductId, CancellationToken cancellationToken);

    void AddCategory(Category category);

    void AddProduct(GlobalProduct product);

    void AddStoreProduct(StoreProduct storeProduct);

    /// <summary>
    /// Detaches a tracked StoreProduct after a failed insert so the winner can be reloaded.
    /// </summary>
    void DiscardTracked(StoreProduct storeProduct);

    Task<Category?> GetCategoryAsync(Guid id, CancellationToken cancellationToken);

    Task<GlobalProduct?> GetProductAsync(Guid id, CancellationToken cancellationToken);

    Task<StoreProduct?> GetStoreProductAsync(Guid storeId, Guid globalProductId, CancellationToken cancellationToken);

    Task<CategoryDto?> GetCategoryDtoAsync(Guid id, CancellationToken cancellationToken);

    Task<GlobalProductDto?> GetProductDtoAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<CategoryDto>> ListCategoriesAsync(CategoryListQuery query, CancellationToken cancellationToken);

    Task<PagedResult<GlobalProductDto>> ListProductsAsync(GlobalProductListQuery query, CancellationToken cancellationToken);

    Task<PagedResult<CatalogProductDto>> BrowseAvailableProductsAsync(
        BrowseCatalogQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoreProductDto>> ListStoreProductsAsync(Guid storeId, CancellationToken cancellationToken);
}
