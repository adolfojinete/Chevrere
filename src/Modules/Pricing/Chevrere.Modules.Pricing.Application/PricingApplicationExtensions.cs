using Chevrere.Modules.Pricing.Application.Commands;
using Chevrere.Modules.Pricing.Application.Contracts;
using Chevrere.Modules.Pricing.Application.Queries;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Modules.Pricing.Application;

public static class PricingApplicationExtensions
{
    public static IServiceCollection AddPricingApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(PricingApplicationExtensions).Assembly, includeInternalTypes: true);
        services.AddScoped<IHandler<SetGlobalSuggestedPriceCommand, Result<GlobalPriceDto>>, SetGlobalSuggestedPriceHandler>();
        services.AddScoped<IHandler<GetGlobalSuggestedPriceQuery, Result<GlobalPriceDto>>, GetGlobalSuggestedPriceHandler>();
        services.AddScoped<IHandler<GlobalPriceHistoryQuery, Result<PagedResult<PriceHistoryItemDto>>>, ListGlobalPriceHistoryHandler>();
        services.AddScoped<IHandler<SetStoreOverridePriceCommand, Result<StoreEffectivePriceDto>>, SetStoreOverridePriceHandler>();
        services.AddScoped<IHandler<RemoveStoreOverridePriceCommand, Result>, RemoveStoreOverridePriceHandler>();
        services.AddScoped<IHandler<GetStoreEffectivePriceQuery, Result<StoreEffectivePriceDto>>, GetStoreEffectivePriceHandler>();
        services.AddScoped<IHandler<StorePriceHistoryQuery, Result<PagedResult<PriceHistoryItemDto>>>, ListStorePriceHistoryHandler>();
        services.AddScoped<IHandler<ListStorePricesQuery, Result<PagedResult<StorePriceListItemDto>>>, ListStorePricesHandler>();
        return services;
    }
}
