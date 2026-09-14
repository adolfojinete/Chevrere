using Chevrere.Modules.Consumer.Application;
using Chevrere.Modules.Consumer.Application.Abstractions;
using Chevrere.Modules.Consumer.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Modules.Consumer.Infrastructure;

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
