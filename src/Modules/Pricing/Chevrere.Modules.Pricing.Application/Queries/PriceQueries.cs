using Chevrere.Modules.Pricing.Application.Abstractions;
using Chevrere.Modules.Pricing.Application.Contracts;
using Chevrere.Modules.Pricing.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Domain.ValueObjects;
using Chevrere.SharedKernel.Results;

namespace Chevrere.Modules.Pricing.Application.Queries;

public sealed record GetGlobalSuggestedPriceQuery(Guid GlobalProductId);

public sealed class GetGlobalSuggestedPriceHandler(IPricingStore store, IPricingCatalogAccess catalog)
    : IHandler<GetGlobalSuggestedPriceQuery, Result<GlobalPriceDto>>
{
    public async Task<Result<GlobalPriceDto>> HandleAsync(
        GetGlobalSuggestedPriceQuery request,
        CancellationToken cancellationToken)
    {
        if (!await catalog.GlobalProductExistsAsync(request.GlobalProductId, cancellationToken))
        {
            return Result.Failure<GlobalPriceDto>(Error.NotFound(ErrorCodes.NotFound, "Product not found."));
        }

        var current = await store.GetCurrentGlobalPriceAsync(request.GlobalProductId, cancellationToken);
        if (current is null)
        {
            return Result.Success(new GlobalPriceDto(request.GlobalProductId, null, null, null));
        }

        return Result.Success(new GlobalPriceDto(
            current.GlobalProductId,
            current.AsMoney().ToDto(),
            current.ValidFrom,
            current.UpdatedAt));
    }
}

public sealed record GlobalPriceHistoryQuery(Guid GlobalProductId, int Page = 1, int PageSize = 20);

public sealed class ListGlobalPriceHistoryHandler(IPricingStore store, IPricingCatalogAccess catalog)
    : IHandler<GlobalPriceHistoryQuery, Result<PagedResult<PriceHistoryItemDto>>>
{
    public async Task<Result<PagedResult<PriceHistoryItemDto>>> HandleAsync(
        GlobalPriceHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (!await catalog.GlobalProductExistsAsync(request.GlobalProductId, cancellationToken))
        {
            return Result.Failure<PagedResult<PriceHistoryItemDto>>(
                Error.NotFound(ErrorCodes.NotFound, "Product not found."));
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var (items, total) = await store.ListGlobalPriceHistoryAsync(
            request.GlobalProductId,
            page,
            pageSize,
            cancellationToken);

        var dtos = items
            .Select(p => new PriceHistoryItemDto(p.Id, p.AsMoney().ToDto(), p.ValidFrom, p.ValidTo))
            .ToList();

        return Result.Success(new PagedResult<PriceHistoryItemDto>(dtos, page, pageSize, total));
    }
}

public sealed record GetStoreEffectivePriceQuery(Guid StoreId, Guid GlobalProductId);

public sealed class GetStoreEffectivePriceHandler(
    IPricingStore store,
    IPricingStoreAccess storeAccess,
    IPricingCatalogAccess catalog,
    ICurrentUser currentUser)
    : IHandler<GetStoreEffectivePriceQuery, Result<StoreEffectivePriceDto>>
{
    public async Task<Result<StoreEffectivePriceDto>> HandleAsync(
        GetStoreEffectivePriceQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<StoreEffectivePriceDto>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var storeTenantId = await storeAccess.GetStoreTenantIdAsync(request.StoreId, cancellationToken);
        if (storeTenantId is null || storeTenantId != tenantId)
        {
            return Result.Failure<StoreEffectivePriceDto>(Error.NotFound(ErrorCodes.NotFound, "Store not found."));
        }

        if (!await catalog.GlobalProductExistsAsync(request.GlobalProductId, cancellationToken))
        {
            return Result.Failure<StoreEffectivePriceDto>(Error.NotFound(ErrorCodes.NotFound, "Product not found."));
        }

        if (!await storeAccess.StoreProductExistsAsync(tenantId, request.StoreId, request.GlobalProductId, cancellationToken))
        {
            return Result.Failure<StoreEffectivePriceDto>(
                Error.NotFound(ErrorCodes.NotFound, "Store product not found."));
        }

        var suggested = await store.GetCurrentGlobalPriceAsync(request.GlobalProductId, cancellationToken);
        var overridePrice = await store.GetCurrentStorePriceAsync(request.StoreId, request.GlobalProductId, cancellationToken);
        var (effective, source) = EffectivePrice.Resolve(suggested?.AsMoney(), overridePrice?.AsMoney());

        return Result.Success(new StoreEffectivePriceDto(
            request.StoreId,
            request.GlobalProductId,
            suggested?.AsMoney().ToDto(),
            overridePrice?.AsMoney().ToDto(),
            effective.ToNullableDto(),
            effective?.Currency,
            source));
    }
}

public sealed record StorePriceHistoryQuery(Guid StoreId, Guid GlobalProductId, int Page = 1, int PageSize = 20);

public sealed class ListStorePriceHistoryHandler(
    IPricingStore store,
    IPricingStoreAccess storeAccess,
    ICurrentUser currentUser)
    : IHandler<StorePriceHistoryQuery, Result<PagedResult<PriceHistoryItemDto>>>
{
    public async Task<Result<PagedResult<PriceHistoryItemDto>>> HandleAsync(
        StorePriceHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<PagedResult<PriceHistoryItemDto>>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var storeTenantId = await storeAccess.GetStoreTenantIdAsync(request.StoreId, cancellationToken);
        if (storeTenantId is null || storeTenantId != tenantId)
        {
            return Result.Failure<PagedResult<PriceHistoryItemDto>>(
                Error.NotFound(ErrorCodes.NotFound, "Store not found."));
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var (items, total) = await store.ListStorePriceHistoryAsync(
            request.StoreId,
            request.GlobalProductId,
            page,
            pageSize,
            cancellationToken);

        var dtos = items
            .Select(p => new PriceHistoryItemDto(p.Id, p.AsMoney().ToDto(), p.ValidFrom, p.ValidTo))
            .ToList();

        return Result.Success(new PagedResult<PriceHistoryItemDto>(dtos, page, pageSize, total));
    }
}

public sealed record ListStorePricesQuery(Guid StoreId, int Page = 1, int PageSize = 20);

public sealed class ListStorePricesHandler(
    IPricingStore store,
    IPricingStoreAccess storeAccess,
    ICurrentUser currentUser)
    : IHandler<ListStorePricesQuery, Result<PagedResult<StorePriceListItemDto>>>
{
    public async Task<Result<PagedResult<StorePriceListItemDto>>> HandleAsync(
        ListStorePricesQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<PagedResult<StorePriceListItemDto>>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var storeTenantId = await storeAccess.GetStoreTenantIdAsync(request.StoreId, cancellationToken);
        if (storeTenantId is null || storeTenantId != tenantId)
        {
            return Result.Failure<PagedResult<StorePriceListItemDto>>(
                Error.NotFound(ErrorCodes.NotFound, "Store not found."));
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var total = await store.CountStoreProductsAsync(request.StoreId, cancellationToken);
        var rows = await store.ListStorePricesAsync(request.StoreId, page, pageSize, cancellationToken);

        var items = rows.Select(row =>
        {
            Money? suggested = row.SuggestedAmount is { } sa && row.SuggestedCurrency is { } sc
                ? Money.FromPersistence(sa, sc)
                : null;
            Money? overridePrice = row.OverrideAmount is { } oa && row.OverrideCurrency is { } oc
                ? Money.FromPersistence(oa, oc)
                : null;
            var (effective, source) = EffectivePrice.Resolve(suggested, overridePrice);
            return new StorePriceListItemDto(
                row.StoreId,
                row.GlobalProductId,
                row.Sku,
                row.Name,
                suggested.ToNullableDto(),
                overridePrice.ToNullableDto(),
                effective.ToNullableDto(),
                effective?.Currency,
                source);
        }).ToList();

        return Result.Success(new PagedResult<StorePriceListItemDto>(items, page, pageSize, total));
    }
}
