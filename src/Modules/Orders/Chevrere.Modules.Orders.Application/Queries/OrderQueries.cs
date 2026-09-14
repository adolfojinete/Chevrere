using Chevrere.Modules.Orders.Application.Abstractions;
using Chevrere.Modules.Orders.Application.Contracts;
using Chevrere.Modules.Orders.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Results;

namespace Chevrere.Modules.Orders.Application.Queries;

public sealed record ListConsumerOrdersQuery(int Page = 1, int PageSize = 20);

public sealed class ListConsumerOrdersHandler(IOrderStore store, ICurrentUser currentUser)
    : IHandler<ListConsumerOrdersQuery, Result<PagedResult<ConsumerOrderSummaryDto>>>
{
    public async Task<Result<PagedResult<ConsumerOrderSummaryDto>>> HandleAsync(
        ListConsumerOrdersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = Commands.OrderConsumer.RequireUser(currentUser);
        if (!user.IsSuccess)
        {
            return Result.Failure<PagedResult<ConsumerOrderSummaryDto>>(user.Error!);
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var (items, total) = await store.ListConsumerOrdersAsync(user.Value, page, pageSize, cancellationToken);
        return Result.Success(new PagedResult<ConsumerOrderSummaryDto>(
            [.. items.Select(OrderMapping.ToConsumerSummary)], page, pageSize, total));
    }
}

public sealed record GetConsumerOrderQuery(Guid OrderId);

public sealed class GetConsumerOrderHandler(IOrderStore store, ICurrentUser currentUser)
    : IHandler<GetConsumerOrderQuery, Result<ConsumerOrderDto>>
{
    public async Task<Result<ConsumerOrderDto>> HandleAsync(
        GetConsumerOrderQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = Commands.OrderConsumer.RequireUser(currentUser);
        if (!user.IsSuccess)
        {
            return Result.Failure<ConsumerOrderDto>(user.Error!);
        }

        var order = await store.GetOrderForConsumerAsync(request.OrderId, user.Value, cancellationToken);
        return order is null
            ? Result.Failure<ConsumerOrderDto>(Error.NotFound(ErrorCodes.NotFound, "Order not found."))
            : Result.Success(OrderMapping.ToConsumerDto(order));
    }
}

public sealed record ListBusinessOrdersQuery(Guid StoreId, int Page = 1, int PageSize = 20);

public sealed class ListBusinessOrdersHandler(
    IOrderStore store,
    IOrderStoreAccess storeAccess,
    ICurrentUser currentUser)
    : IHandler<ListBusinessOrdersQuery, Result<PagedResult<BusinessOrderSummaryDto>>>
{
    public async Task<Result<PagedResult<BusinessOrderSummaryDto>>> HandleAsync(
        ListBusinessOrdersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<PagedResult<BusinessOrderSummaryDto>>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        if (await storeAccess.GetStoreTenantIdAsync(request.StoreId, cancellationToken) is not Guid storeTenant
            || storeTenant != tenantId)
        {
            return Result.Failure<PagedResult<BusinessOrderSummaryDto>>(
                Error.NotFound(ErrorCodes.NotFound, "Store not found."));
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var (items, total) = await store.ListStoreOrdersAsync(tenantId, request.StoreId, page, pageSize, cancellationToken);
        return Result.Success(new PagedResult<BusinessOrderSummaryDto>(
            [.. items.Select(OrderMapping.ToBusinessSummary)], page, pageSize, total));
    }
}

public sealed record GetBusinessOrderQuery(Guid StoreId, Guid OrderId);

public sealed class GetBusinessOrderHandler(
    IOrderStore store,
    IOrderStoreAccess storeAccess,
    ICurrentUser currentUser)
    : IHandler<GetBusinessOrderQuery, Result<BusinessOrderDto>>
{
    public async Task<Result<BusinessOrderDto>> HandleAsync(
        GetBusinessOrderQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<BusinessOrderDto>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        if (await storeAccess.GetStoreTenantIdAsync(request.StoreId, cancellationToken) is not Guid storeTenant
            || storeTenant != tenantId)
        {
            return Result.Failure<BusinessOrderDto>(Error.NotFound(ErrorCodes.NotFound, "Order not found."));
        }

        var order = await store.GetOrderForStoreAsync(tenantId, request.StoreId, request.OrderId, cancellationToken);
        return order is null
            ? Result.Failure<BusinessOrderDto>(Error.NotFound(ErrorCodes.NotFound, "Order not found."))
            : Result.Success(OrderMapping.ToBusinessDto(order));
    }
}

public sealed record GetAdminOrderQuery(Guid OrderId);

public sealed class GetAdminOrderHandler(IOrderStore store)
    : IHandler<GetAdminOrderQuery, Result<AdminOrderDto>>
{
    public async Task<Result<AdminOrderDto>> HandleAsync(
        GetAdminOrderQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var order = await store.GetOrderAsync(request.OrderId, cancellationToken);
        return order is null
            ? Result.Failure<AdminOrderDto>(Error.NotFound(ErrorCodes.NotFound, "Order not found."))
            : Result.Success(OrderMapping.ToAdminDto(order));
    }
}
