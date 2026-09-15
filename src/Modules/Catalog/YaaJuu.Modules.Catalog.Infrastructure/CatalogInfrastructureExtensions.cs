using YaaJuu.Modules.Catalog.Application;
using YaaJuu.Modules.Catalog.Application.Abstractions;
using YaaJuu.Modules.Catalog.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.Modules.Catalog.Infrastructure;

public static class CatalogInfrastructureExtensions
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddCatalogApplication();
        services.AddScoped<ICatalogStore, CatalogStore>();
        services.AddScoped<IStoreAccess, StoreAccess>();
        return services;
    }
}
