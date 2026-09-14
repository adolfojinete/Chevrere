using Chevrere.Modules.Pricing.Application;
using Chevrere.Modules.Pricing.Application.Abstractions;
using Chevrere.Modules.Pricing.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Modules.Pricing.Infrastructure;

public static class PricingInfrastructureExtensions
{
    public static IServiceCollection AddPricingModule(this IServiceCollection services)
    {
        services.AddPricingApplication();
        services.AddScoped<IPricingStore, PricingStore>();
        services.AddScoped<IPricingCatalogAccess, PricingCatalogAccess>();
        services.AddScoped<IPricingStoreAccess, PricingStoreAccess>();
        return services;
    }
}
