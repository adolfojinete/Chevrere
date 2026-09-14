using Chevrere.Modules.Inventory.Application.Abstractions;
using Chevrere.Modules.Inventory.Application.Contracts;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Results;

namespace Chevrere.Modules.Inventory.Application.Queries;

public sealed record ListStoreInventoryQuery(
    Guid StoreId,
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    bool? Initialized = null,
    bool? Enabled = null,
    string? SortBy = null);

public sealed class ListStoreInventoryHandler(IInventoryStore store, IInventoryStoreAccess storeAccess, ICurrentUser currentUser)
    : IHandler<ListStoreInventoryQuery, Result<PagedResult<InventoryItemDto>>>
{
    public async Task<Result<PagedResult<InventoryItemDto>>> HandleAsync(
        ListStoreInventoryQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.IsPlatformUser)
        {
            var exists = await storeAccess.GetStoreTenantIdAsync(request.StoreId, cancellationToken);
            if (exists is null)
            {
                return Result.Failure<PagedResult<InventoryItemDto>>(Error.NotFound(ErrorCodes.NotFound, "Store not found."));
            }
        }
        else
        {
            if (currentUser.TenantId is not Guid tenantId)
            {
                return Result.Failure<PagedResult<InventoryItemDto>>(
                    Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
            }

            var storeTenantId = await storeAccess.GetStoreTenantIdAsync(request.StoreId, cancellationToken);
            if (storeTenantId is null || storeTenantId != tenantId)
            {
                return Result.Failure<PagedResult<InventoryItemDto>>(Error.NotFound(ErrorCodes.NotFound, "Store not found."));
            }
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var (items, total) = await store.ListStoreInventoryAsync(
            request.StoreId, page, pageSize, request.Search, request.Initialized, request.Enabled, request.SortBy, cancellationToken);

        var dtos = items.Select(ToDto).ToList();
        return Result.Success(new PagedResult<InventoryItemDto>(dtos, page, pageSize, total));
    }

    internal static InventoryItemDto ToDto(InventoryListProjection row) =>
        new(
            row.InventoryItemId,
            row.StoreId,
            row.GlobalProductId,
            row.Sku,
            row.Name,
            row.IsStoreProductEnabled,
            row.InventoryItemId is not null,
            row.OnHand ?? 0,
            row.Reserved ?? 0,
            (row.OnHand ?? 0) - (row.Reserved ?? 0));
}

public sealed record GetStoreInventoryItemQuery(Guid StoreId, Guid GlobalProductId);

public sealed class GetStoreInventoryItemHandler(IInventoryStore store, IInventoryStoreAccess storeAccess, ICurrentUser currentUser)
    : IHandler<GetStoreInventoryItemQuery, Result<InventoryItemDto>>
{
    public async Task<Result<InventoryItemDto>> HandleAsync(
        GetStoreInventoryItemQuery request,
        CancellationToken cancellationToken)
    {
        Guid effectiveTenant;
        if (currentUser.IsPlatformUser)
        {
            var storeTenant = await storeAccess.GetStoreTenantIdAsync(request.StoreId, cancellationToken);
            if (storeTenant is null)
            {
                return Result.Failure<InventoryItemDto>(Error.NotFound(ErrorCodes.NotFound, "Store not found."));
            }

            effectiveTenant = storeTenant.Value;
        }
        else
        {
            if (currentUser.TenantId is not Guid tenantId)
            {
                return Result.Failure<InventoryItemDto>(
                    Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
            }

            var storeTenantId = await storeAccess.GetStoreTenantIdAsync(request.StoreId, cancellationToken);
            if (storeTenantId is null || storeTenantId != tenantId)
            {
                return Result.Failure<InventoryItemDto>(Error.NotFound(ErrorCodes.NotFound, "Store not found."));
            }

            effectiveTenant = tenantId;
        }

        if (!await storeAccess.StoreProductExistsAsync(effectiveTenant, request.StoreId, request.GlobalProductId, cancellationToken))
        {
            return Result.Failure<InventoryItemDto>(
                Error.Conflict("inventory.product.not_offered", "The store has not enabled this product."));
        }

        var projection = await store.GetStoreProductProjectionAsync(request.StoreId, request.GlobalProductId, cancellationToken);
        if (projection is null)
        {
            return Result.Failure<InventoryItemDto>(
                Error.Conflict("inventory.product.not_offered", "The store has not enabled this product."));
        }

        return Result.Success(ListStoreInventoryHandler.ToDto(projection));
    }
}

public sealed record ListInventoryMovementsQuery(Guid StoreId, Guid GlobalProductId, int Page = 1, int PageSize = 20);

public sealed class ListInventoryMovementsHandler(IInventoryStore store, IInventoryStoreAccess storeAccess, ICurrentUser currentUser)
    : IHandler<ListInventoryMovementsQuery, Result<PagedResult<InventoryMovementDto>>>
{
    public async Task<Result<PagedResult<InventoryMovementDto>>> HandleAsync(
        ListInventoryMovementsQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<PagedResult<InventoryMovementDto>>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var storeTenantId = await storeAccess.GetStoreTenantIdAsync(request.StoreId, cancellationToken);
        if (storeTenantId is null || storeTenantId != tenantId)
        {
            return Result.Failure<PagedResult<InventoryMovementDto>>(Error.NotFound(ErrorCodes.NotFound, "Store not found."));
        }

        if (!await storeAccess.StoreProductExistsAsync(tenantId, request.StoreId, request.GlobalProductId, cancellationToken))
        {
            return Result.Failure<PagedResult<InventoryMovementDto>>(
                Error.Conflict("inventory.product.not_offered", "The store has not enabled this product."));
        }

        var item = await store.GetItemAsync(request.StoreId, request.GlobalProductId, cancellationToken);
        if (item is null)
        {
            return Result.Success(new PagedResult<InventoryMovementDto>([], Math.Max(1, request.Page), Math.Clamp(request.PageSize, 1, 100), 0));
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var (movements, total) = await store.ListMovementsAsync(item.Id, page, pageSize, cancellationToken);
        var dtos = movements.Select(m => new InventoryMovementDto(
            m.Id,
            m.Type,
            m.OnHandDelta,
            m.ReservedDelta,
            m.OnHandBefore,
            m.OnHandAfter,
            m.ReservedBefore,
            m.ReservedAfter,
            m.Reason,
            m.ReferenceType,
            m.ReferenceId,
            m.OccurredAt,
            m.ActorUserId)).ToList();

        return Result.Success(new PagedResult<InventoryMovementDto>(dtos, page, pageSize, total));
    }
}
