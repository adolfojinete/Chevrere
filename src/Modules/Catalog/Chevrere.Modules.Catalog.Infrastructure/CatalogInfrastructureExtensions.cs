using Chevrere.Modules.Catalog.Application;
using Chevrere.Modules.Catalog.Application.Abstractions;
using Chevrere.Modules.Catalog.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Modules.Catalog.Infrastructure;

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
