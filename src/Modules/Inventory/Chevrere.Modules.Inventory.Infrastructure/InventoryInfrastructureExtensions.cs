using Chevrere.Modules.Inventory.Application;
using Chevrere.Modules.Inventory.Application.Abstractions;
using Chevrere.Modules.Inventory.Infrastructure.Persistence;
using Chevrere.SharedKernel.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Modules.Inventory.Infrastructure;

public static class InventoryInfrastructureExtensions
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddInventoryApplication();
        services.AddScoped<IInventoryStore, InventoryStore>();
        services.AddScoped<IInventoryStoreAccess, InventoryStoreAccess>();
        services.AddScoped<IInventoryInboundService, InventoryInboundService>();
        services.AddScoped<IInventoryReservationService, InventoryReservationService>();
        return services;
    }
}
