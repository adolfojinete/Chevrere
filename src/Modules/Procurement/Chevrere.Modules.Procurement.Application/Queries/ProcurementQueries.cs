using Chevrere.Modules.Procurement.Application.Abstractions;
using Chevrere.Modules.Procurement.Application.Contracts;
using Chevrere.Modules.Procurement.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Results;

namespace Chevrere.Modules.Procurement.Application.Queries;

public sealed record ListSuppliersQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    bool? IsActive = null);

public sealed class ListSuppliersHandler(IProcurementStore store, ICurrentUser currentUser)
    : IHandler<ListSuppliersQuery, Result<PagedResult<SupplierDto>>>
{
    public async Task<Result<PagedResult<SupplierDto>>> HandleAsync(
        ListSuppliersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<PagedResult<SupplierDto>>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var (items, total) = await store.ListSuppliersAsync(
            tenantId, page, pageSize, request.Search, request.IsActive, cancellationToken);

        var dtos = items.Select(ProcurementMapping.ToDto).ToList();
        return Result.Success(new PagedResult<SupplierDto>(dtos, page, pageSize, total));
    }
}

public sealed record GetSupplierQuery(Guid SupplierId);

public sealed class GetSupplierHandler(IProcurementStore store, ICurrentUser currentUser)
    : IHandler<GetSupplierQuery, Result<SupplierDto>>
{
    public async Task<Result<SupplierDto>> HandleAsync(GetSupplierQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<SupplierDto>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var supplier = await store.GetSupplierAsync(request.SupplierId, cancellationToken);
        if (supplier is null || supplier.TenantId != tenantId)
        {
            return Result.Failure<SupplierDto>(Error.NotFound(ErrorCodes.NotFound, "Supplier not found."));
        }

        return Result.Success(ProcurementMapping.ToDto(supplier));
    }
}

public sealed record ListPurchaseOrdersQuery(
    Guid StoreId,
    int Page = 1,
    int PageSize = 20,
    PurchaseOrderStatus? Status = null,
    Guid? SupplierId = null);

public sealed class ListPurchaseOrdersHandler(
    IProcurementStore store,
    IProcurementStoreAccess storeAccess,
    ICurrentUser currentUser)
    : IHandler<ListPurchaseOrdersQuery, Result<PagedResult<PurchaseOrderSummaryDto>>>
{
    public async Task<Result<PagedResult<PurchaseOrderSummaryDto>>> HandleAsync(
        ListPurchaseOrdersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (tenantId, error) = await ProcurementQueryScope.ResolveAsync(
            storeAccess, currentUser, request.StoreId, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<PagedResult<PurchaseOrderSummaryDto>>(error);
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var (items, total) = await store.ListPurchaseOrdersAsync(
            tenantId, request.StoreId, page, pageSize, request.Status, request.SupplierId, cancellationToken);

        var dtos = items.Select(ProcurementMapping.ToDto).ToList();
        return Result.Success(new PagedResult<PurchaseOrderSummaryDto>(dtos, page, pageSize, total));
    }
}

public sealed record GetPurchaseOrderQuery(Guid StoreId, Guid PurchaseOrderId);

public sealed class GetPurchaseOrderHandler(
    IProcurementStore store,
    IProcurementStoreAccess storeAccess,
    ICurrentUser currentUser)
    : IHandler<GetPurchaseOrderQuery, Result<PurchaseOrderDto>>
{
    public async Task<Result<PurchaseOrderDto>> HandleAsync(
        GetPurchaseOrderQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (tenantId, error) = await ProcurementQueryScope.ResolveAsync(
            storeAccess, currentUser, request.StoreId, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<PurchaseOrderDto>(error);
        }

        var order = await store.ReadPurchaseOrderAsync(request.PurchaseOrderId, cancellationToken);
        if (order is null || !order.BelongsTo(tenantId, request.StoreId))
        {
            return Result.Failure<PurchaseOrderDto>(Error.NotFound(ErrorCodes.NotFound, "Purchase order not found."));
        }

        return Result.Success(ProcurementMapping.ToDto(order));
    }
}

public sealed record ListGoodsReceiptsQuery(Guid StoreId, Guid PurchaseOrderId, int Page = 1, int PageSize = 20);

public sealed class ListGoodsReceiptsHandler(
    IProcurementStore store,
    IProcurementStoreAccess storeAccess,
    ICurrentUser currentUser)
    : IHandler<ListGoodsReceiptsQuery, Result<PagedResult<GoodsReceiptSummaryDto>>>
{
    public async Task<Result<PagedResult<GoodsReceiptSummaryDto>>> HandleAsync(
        ListGoodsReceiptsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (tenantId, error) = await ProcurementQueryScope.ResolveAsync(
            storeAccess, currentUser, request.StoreId, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<PagedResult<GoodsReceiptSummaryDto>>(error);
        }

        var order = await store.ReadPurchaseOrderAsync(request.PurchaseOrderId, cancellationToken);
        if (order is null || !order.BelongsTo(tenantId, request.StoreId))
        {
            return Result.Failure<PagedResult<GoodsReceiptSummaryDto>>(
                Error.NotFound(ErrorCodes.NotFound, "Purchase order not found."));
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var (items, total) = await store.ListGoodsReceiptsAsync(
            tenantId, request.PurchaseOrderId, page, pageSize, cancellationToken);

        var dtos = items.Select(ProcurementMapping.ToDto).ToList();
        return Result.Success(new PagedResult<GoodsReceiptSummaryDto>(dtos, page, pageSize, total));
    }
}

public sealed record GetGoodsReceiptQuery(Guid StoreId, Guid GoodsReceiptId);

public sealed class GetGoodsReceiptHandler(
    IProcurementStore store,
    IProcurementStoreAccess storeAccess,
    ICurrentUser currentUser)
    : IHandler<GetGoodsReceiptQuery, Result<GoodsReceiptDto>>
{
    public async Task<Result<GoodsReceiptDto>> HandleAsync(
        GetGoodsReceiptQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (tenantId, error) = await ProcurementQueryScope.ResolveAsync(
            storeAccess, currentUser, request.StoreId, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<GoodsReceiptDto>(error);
        }

        var receipt = await store.ReadGoodsReceiptAsync(request.GoodsReceiptId, cancellationToken);
        if (receipt is null || receipt.TenantId != tenantId || receipt.StoreId != request.StoreId)
        {
            return Result.Failure<GoodsReceiptDto>(Error.NotFound(ErrorCodes.NotFound, "Goods receipt not found."));
        }

        return Result.Success(ProcurementMapping.ToDto(receipt));
    }
}

internal static class ProcurementQueryScope
{
    /// <summary>
    /// Resolves the tenant a read is scoped to. Platform staff inherit the tenant of the store they
    /// ask about; a business user may only read stores of its own tenant.
    /// </summary>
    public static async Task<(Guid TenantId, Error? Error)> ResolveAsync(
        IProcurementStoreAccess storeAccess,
        ICurrentUser currentUser,
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var storeTenantId = await storeAccess.GetStoreTenantIdAsync(storeId, cancellationToken);
        if (storeTenantId is null)
        {
            return (Guid.Empty, Error.NotFound(ErrorCodes.NotFound, "Store not found."));
        }

        if (currentUser.IsPlatformUser)
        {
            return (storeTenantId.Value, null);
        }

        if (currentUser.TenantId is not Guid tenantId)
        {
            return (Guid.Empty, Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        return storeTenantId.Value != tenantId
            ? (Guid.Empty, Error.NotFound(ErrorCodes.NotFound, "Store not found."))
            : (tenantId, null);
    }
}
