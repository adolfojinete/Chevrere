using Chevrere.Modules.Catalog.Application.Abstractions;
using Chevrere.Modules.Catalog.Application.Contracts;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Results;

namespace Chevrere.Modules.Catalog.Application.Queries;

public sealed record GetCategoryQuery(Guid Id);

public sealed record GetGlobalProductQuery(Guid Id);

public sealed class GetCategoryHandler(ICatalogStore store)
    : IHandler<GetCategoryQuery, Result<CategoryDto>>
{
    public async Task<Result<CategoryDto>> HandleAsync(GetCategoryQuery request, CancellationToken cancellationToken)
    {
        var dto = await store.GetCategoryDtoAsync(request.Id, cancellationToken);
        return dto is null
            ? Result.Failure<CategoryDto>(Error.NotFound(ErrorCodes.NotFound, "Category not found."))
            : Result.Success(dto);
    }
}

public sealed class ListCategoriesHandler(ICatalogStore store)
    : IHandler<CategoryListQuery, PagedResult<CategoryDto>>
{
    public Task<PagedResult<CategoryDto>> HandleAsync(CategoryListQuery request, CancellationToken cancellationToken)
        => store.ListCategoriesAsync(request, cancellationToken);
}

public sealed class GetGlobalProductHandler(ICatalogStore store)
    : IHandler<GetGlobalProductQuery, Result<GlobalProductDto>>
{
    public async Task<Result<GlobalProductDto>> HandleAsync(GetGlobalProductQuery request, CancellationToken cancellationToken)
    {
        var dto = await store.GetProductDtoAsync(request.Id, cancellationToken);
        return dto is null
            ? Result.Failure<GlobalProductDto>(Error.NotFound(ErrorCodes.NotFound, "Product not found."))
            : Result.Success(dto);
    }
}

public sealed class ListGlobalProductsHandler(ICatalogStore store)
    : IHandler<GlobalProductListQuery, PagedResult<GlobalProductDto>>
{
    public Task<PagedResult<GlobalProductDto>> HandleAsync(
        GlobalProductListQuery request,
        CancellationToken cancellationToken)
        => store.ListProductsAsync(request, cancellationToken);
}

public sealed class BrowseGlobalCatalogHandler(ICatalogStore store, ICurrentUser currentUser)
    : IHandler<BrowseCatalogQuery, Result<PagedResult<CatalogProductDto>>>
{
    public async Task<Result<PagedResult<CatalogProductDto>>> HandleAsync(
        BrowseCatalogQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<PagedResult<CatalogProductDto>>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var page = await store.BrowseAvailableProductsAsync(request, cancellationToken);
        return Result.Success(page);
    }
}

public sealed class ListStoreProductsHandler(ICatalogStore store, IStoreAccess storeAccess, ICurrentUser currentUser)
    : IHandler<ListStoreProductsQuery, Result<IReadOnlyList<StoreProductDto>>>
{
    public async Task<Result<IReadOnlyList<StoreProductDto>>> HandleAsync(
        ListStoreProductsQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<IReadOnlyList<StoreProductDto>>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var storeTenantId = await storeAccess.GetStoreTenantIdAsync(request.StoreId, cancellationToken);
        if (storeTenantId is null || storeTenantId != tenantId)
        {
            return Result.Failure<IReadOnlyList<StoreProductDto>>(Error.NotFound(ErrorCodes.NotFound, "Store not found."));
        }

        var items = await store.ListStoreProductsAsync(request.StoreId, cancellationToken);
        return Result.Success(items);
    }
}
