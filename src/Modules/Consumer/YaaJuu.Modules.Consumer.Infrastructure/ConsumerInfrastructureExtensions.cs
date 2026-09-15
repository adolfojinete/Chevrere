using YaaJuu.Modules.Consumer.Application;
using YaaJuu.Modules.Consumer.Application.Abstractions;
using YaaJuu.Modules.Consumer.Infrastructure.Persistence;
using YaaJuu.SharedKernel.Discovery;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.Modules.Consumer.Infrastructure;

public static class ConsumerInfrastructureExtensions
{
    public static IServiceCollection AddConsumerModule(this IServiceCollection services)
    {
        services.AddConsumerApplication();
        services.AddScoped<IStoreServiceAreaStore, StoreServiceAreaStore>();
        services.AddScoped<IConsumerStoreAccess, ConsumerStoreAccess>();
        services.AddScoped<IConsumerStoreResolver, ConsumerStoreResolver>();
        services.AddScoped<IConsumerCatalogReadStore, ConsumerCatalogReadStore>();
        return services;
    }
}
