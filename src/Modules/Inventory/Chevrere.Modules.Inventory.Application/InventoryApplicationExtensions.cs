using Chevrere.Modules.Inventory.Application.Commands;
using Chevrere.Modules.Inventory.Application.Contracts;
using Chevrere.Modules.Inventory.Application.Queries;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Modules.Inventory.Application;

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
