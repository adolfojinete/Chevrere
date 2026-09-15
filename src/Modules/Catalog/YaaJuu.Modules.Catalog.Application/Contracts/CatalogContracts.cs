using YaaJuu.Modules.Catalog.Domain;

namespace YaaJuu.Modules.Catalog.Application.Contracts;

public sealed record CreateCategoryRequest(
    string Code,
    string Name,
    string? Description,
    int SortOrder);

public sealed record UpdateCategoryRequest(
    string Name,
    string? Description,
    int SortOrder);

public sealed record CategoryDto(
    Guid Id,
    string Code,
    string Name,
    string Slug,
    string? Description,
    CategoryStatus Status,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateGlobalProductRequest(
    Guid CategoryId,
    string Sku,
    string Name,
    string Brand,
    string Presentation,
    string? Description,
    string? Barcode);

public sealed record UpdateGlobalProductRequest(
    Guid CategoryId,
    string Name,
    string Brand,
    string Presentation,
    string? Description,
    string? Barcode);

public sealed record GlobalProductDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Sku,
    string Name,
    string Slug,
    string Brand,
    string Presentation,
    string? Description,
    string? Barcode,
    GlobalProductStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CatalogProductDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Sku,
    string Name,
    string Brand,
    string Presentation,
    string? Description);

public sealed record StoreProductDto(
    Guid Id,
    Guid StoreId,
    Guid GlobalProductId,
    string Sku,
    string Name,
    string Brand,
    string Presentation,
    string CategoryName,
    bool IsEnabled,
    bool IsCommerciallyAvailable,
    DateTimeOffset UpdatedAt);

public sealed record CategoryListQuery(
    int Page,
    int PageSize,
    string? Search,
    CategoryStatus? Status);

public sealed record GlobalProductListQuery(
    int Page,
    int PageSize,
    string? Search,
    Guid? CategoryId,
    GlobalProductStatus? Status,
    string? SortBy);

public sealed record BrowseCatalogQuery(
    int Page,
    int PageSize,
    string? Search,
    Guid? CategoryId,
    string? SortBy);

public sealed record ListStoreProductsQuery(Guid StoreId);
