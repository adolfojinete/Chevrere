using YaaJuu.Modules.Pricing.Application;
using YaaJuu.Modules.Pricing.Application.Abstractions;
using YaaJuu.Modules.Pricing.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.Modules.Pricing.Infrastructure;

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
