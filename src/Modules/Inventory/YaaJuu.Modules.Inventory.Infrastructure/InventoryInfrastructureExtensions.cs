using YaaJuu.Modules.Inventory.Application;
using YaaJuu.Modules.Inventory.Application.Abstractions;
using YaaJuu.Modules.Inventory.Infrastructure.Persistence;
using YaaJuu.SharedKernel.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.Modules.Inventory.Infrastructure;

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
