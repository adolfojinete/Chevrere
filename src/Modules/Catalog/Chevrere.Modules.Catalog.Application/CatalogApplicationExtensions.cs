using Chevrere.Modules.Catalog.Application.Commands;
using Chevrere.Modules.Catalog.Application.Contracts;
using Chevrere.Modules.Catalog.Application.Queries;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Modules.Catalog.Application;

public static class CatalogApplicationExtensions
{
    public static IServiceCollection AddCatalogApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(CatalogApplicationExtensions).Assembly, includeInternalTypes: true);
        services.AddScoped<IHandler<CreateCategoryRequest, Result<CategoryDto>>, CreateCategoryHandler>();
        services.AddScoped<IHandler<UpdateCategoryCommand, Result<CategoryDto>>, UpdateCategoryHandler>();
        services.AddScoped<IHandler<ActivateCategoryCommand, Result>, ActivateCategoryHandler>();
        services.AddScoped<IHandler<DeactivateCategoryCommand, Result>, DeactivateCategoryHandler>();
        services.AddScoped<IHandler<GetCategoryQuery, Result<CategoryDto>>, GetCategoryHandler>();
        services.AddScoped<IHandler<CategoryListQuery, PagedResult<CategoryDto>>, ListCategoriesHandler>();
        services.AddScoped<IHandler<CreateGlobalProductRequest, Result<GlobalProductDto>>, CreateGlobalProductHandler>();
        services.AddScoped<IHandler<UpdateGlobalProductCommand, Result<GlobalProductDto>>, UpdateGlobalProductHandler>();
        services.AddScoped<IHandler<ActivateGlobalProductCommand, Result>, ActivateGlobalProductHandler>();
        services.AddScoped<IHandler<DeactivateGlobalProductCommand, Result>, DeactivateGlobalProductHandler>();
        services.AddScoped<IHandler<GetGlobalProductQuery, Result<GlobalProductDto>>, GetGlobalProductHandler>();
        services.AddScoped<IHandler<GlobalProductListQuery, PagedResult<GlobalProductDto>>, ListGlobalProductsHandler>();
        services.AddScoped<IHandler<BrowseCatalogQuery, Result<PagedResult<CatalogProductDto>>>, BrowseGlobalCatalogHandler>();
        services.AddScoped<IHandler<ListStoreProductsQuery, Result<IReadOnlyList<StoreProductDto>>>, ListStoreProductsHandler>();
        services.AddScoped<IHandler<EnableStoreProductCommand, Result<StoreProductDto>>, EnableStoreProductHandler>();
        services.AddScoped<IHandler<DisableStoreProductCommand, Result>, DisableStoreProductHandler>();
        return services;
    }
}
