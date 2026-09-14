using Chevrere.Modules.Orders.Application;
using Chevrere.Modules.Orders.Application.Abstractions;
using Chevrere.Modules.Orders.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Modules.Orders.Infrastructure;

public static class OrdersInfrastructureExtensions
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOrdersApplication();
        services.AddOptions<OrderReservationOptions>()
            .Bind(configuration.GetSection(OrderReservationOptions.SectionName))
            .Validate(
                o => o.ReservationTtlMinutes > 0
                     && o.ExpirationPollSeconds > 0
                     && o.ExpirationBatchSize > 0
                     && o.ExpirationBatchSize <= 500,
                "Orders options must have positive TTL, poll interval and a batch size between 1 and 500.")
            .ValidateOnStart();

        services.AddScoped<IOrderStore, OrderStore>();
        services.AddScoped<IOrderStoreAccess, OrderStoreAccess>();
        services.AddScoped<IOrderNumberGenerator, OrderNumberGenerator>();
        services.AddScoped<IOrderCommercialReadStore, OrderCommercialReadStore>();
        services.AddSingleton<IOrderReservationPolicy, ConfiguredOrderReservationPolicy>();
        services.AddHostedService<OrderExpirationWorker>();
        return services;
    }
}
