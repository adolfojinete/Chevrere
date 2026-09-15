using YaaJuu.Modules.Inventory.Application.Commands;
using YaaJuu.Modules.Inventory.Application.Contracts;
using YaaJuu.Modules.Inventory.Application.Queries;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.Modules.Inventory.Application;

public static class InventoryApplicationExtensions
{
    public static IServiceCollection AddInventoryApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(InventoryApplicationExtensions).Assembly, includeInternalTypes: true);
        services.AddScoped<IHandler<InitializeInventoryCommand, Result<InventoryMutationDto>>, InitializeInventoryHandler>();
        services.AddScoped<IHandler<AdjustInventoryCommand, Result<InventoryMutationDto>>, AdjustInventoryHandler>();
        services.AddScoped<IHandler<WasteInventoryCommand, Result<InventoryMutationDto>>, WasteInventoryHandler>();
        services.AddScoped<IHandler<ListStoreInventoryQuery, Result<PagedResult<InventoryItemDto>>>, ListStoreInventoryHandler>();
        services.AddScoped<IHandler<GetStoreInventoryItemQuery, Result<InventoryItemDto>>, GetStoreInventoryItemHandler>();
        services.AddScoped<IHandler<ListInventoryMovementsQuery, Result<PagedResult<InventoryMovementDto>>>, ListInventoryMovementsHandler>();
        return services;
    }
}
