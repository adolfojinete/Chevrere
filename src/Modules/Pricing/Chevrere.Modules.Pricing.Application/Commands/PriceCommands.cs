using Chevrere.Modules.Pricing.Application.Abstractions;
using Chevrere.Modules.Pricing.Application.Contracts;
using Chevrere.Modules.Pricing.Domain;
using Chevrere.Modules.Pricing.Domain.ValueObjects;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Results;
using Chevrere.SharedKernel.Time;

namespace Chevrere.Modules.Pricing.Application.Commands;

public sealed record SetGlobalSuggestedPriceCommand(Guid GlobalProductId, decimal Amount, string Currency);

public sealed class SetGlobalSuggestedPriceHandler(
    IPricingStore store,
    IPricingCatalogAccess catalog,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<SetGlobalSuggestedPriceCommand, Result<GlobalPriceDto>>
{
    public async Task<Result<GlobalPriceDto>> HandleAsync(
        SetGlobalSuggestedPriceCommand request,
        CancellationToken cancellationToken)
    {
        if (!await catalog.GlobalProductExistsAsync(request.GlobalProductId, cancellationToken))
        {
            return Result.Failure<GlobalPriceDto>(Error.NotFound(ErrorCodes.NotFound, "Product not found."));
        }

        Money money;
        try
        {
            money = Money.Create(request.Amount, request.Currency);
        }
        catch (DomainException ex)
        {
            return Result.Failure<GlobalPriceDto>(Error.Domain(ex.Code, ex.Message));
        }

        var current = await store.GetCurrentGlobalPriceAsync(request.GlobalProductId, cancellationToken);
        if (current is not null && current.AsMoney().Equals(money))
        {
            return Result.Success(ToDto(current));
        }

        var now = clock.UtcNow;
        object? previous = current is null
            ? null
            : new { current.Amount, current.Currency };

        if (current is not null)
        {
            current.Close(now);
        }

        var opened = GlobalProductPrice.Open(request.GlobalProductId, money, now);
        store.AddGlobalPrice(opened);

        audit.Record(
            current is null ? AuditActions.GlobalSuggestedPriceSet : AuditActions.GlobalSuggestedPriceChanged,
            nameof(GlobalProductPrice),
            opened.Id,
            tenantId: null,
            previousValue: previous,
            newValue: new { opened.Amount, opened.Currency });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(opened));
    }

    private static GlobalPriceDto ToDto(GlobalProductPrice price) =>
        new(price.GlobalProductId, price.AsMoney().ToDto(), price.ValidFrom, price.UpdatedAt);
}

public sealed record SetStoreOverridePriceCommand(
    Guid StoreId,
    Guid GlobalProductId,
    decimal Amount,
    string Currency);

public sealed class SetStoreOverridePriceHandler(
    IPricingStore store,
    IPricingStoreAccess storeAccess,
    IPricingCatalogAccess catalog,
    ICurrentUser currentUser,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<SetStoreOverridePriceCommand, Result<StoreEffectivePriceDto>>
{
    public async Task<Result<StoreEffectivePriceDto>> HandleAsync(
        SetStoreOverridePriceCommand request,
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
                Error.Conflict("store_price.product.not_offered", "The store has not enabled this product."));
        }

        Money money;
        try
        {
            money = Money.Create(request.Amount, request.Currency);
        }
        catch (DomainException ex)
        {
            return Result.Failure<StoreEffectivePriceDto>(Error.Domain(ex.Code, ex.Message));
        }

        var current = await store.GetCurrentStorePriceAsync(request.StoreId, request.GlobalProductId, cancellationToken);
        if (current is not null && current.AsMoney().Equals(money))
        {
            return Result.Success(await BuildEffectiveAsync(request.StoreId, request.GlobalProductId, cancellationToken));
        }

        var now = clock.UtcNow;
        object? previous = current is null ? null : new { current.Amount, current.Currency };

        if (current is not null)
        {
            current.Close(now);
        }

        var opened = StoreProductPrice.Open(tenantId, request.StoreId, request.GlobalProductId, money, now);
        store.AddStorePrice(opened);

        audit.Record(
            current is null ? AuditActions.StorePriceSet : AuditActions.StorePriceChanged,
            nameof(StoreProductPrice),
            opened.Id,
            tenantId,
            previousValue: previous,
            newValue: new { opened.Amount, opened.Currency });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await BuildEffectiveAsync(request.StoreId, request.GlobalProductId, cancellationToken));
    }

    private async Task<StoreEffectivePriceDto> BuildEffectiveAsync(
        Guid storeId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var suggested = await store.GetCurrentGlobalPriceAsync(productId, cancellationToken);
        var overridePrice = await store.GetCurrentStorePriceAsync(storeId, productId, cancellationToken);
        var (effective, source) = EffectivePrice.Resolve(suggested?.AsMoney(), overridePrice?.AsMoney());
        return new StoreEffectivePriceDto(
            storeId,
            productId,
            suggested?.AsMoney().ToDto(),
            overridePrice?.AsMoney().ToDto(),
            effective.ToNullableDto(),
            effective?.Currency,
            source);
    }
}

public sealed record RemoveStoreOverridePriceCommand(Guid StoreId, Guid GlobalProductId);

public sealed class RemoveStoreOverridePriceHandler(
    IPricingStore store,
    IPricingStoreAccess storeAccess,
    IPricingCatalogAccess catalog,
    ICurrentUser currentUser,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<RemoveStoreOverridePriceCommand, Result>
{
    public async Task<Result> HandleAsync(RemoveStoreOverridePriceCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure(Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var storeTenantId = await storeAccess.GetStoreTenantIdAsync(request.StoreId, cancellationToken);
        if (storeTenantId is null || storeTenantId != tenantId)
        {
            return Result.Failure(Error.NotFound(ErrorCodes.NotFound, "Store not found."));
        }

        if (!await catalog.GlobalProductExistsAsync(request.GlobalProductId, cancellationToken))
        {
            return Result.Failure(Error.NotFound(ErrorCodes.NotFound, "Product not found."));
        }

        if (!await storeAccess.StoreProductExistsAsync(tenantId, request.StoreId, request.GlobalProductId, cancellationToken))
        {
            return Result.Failure(
                Error.Conflict("store_price.product.not_offered", "The store has not enabled this product."));
        }

        var current = await store.GetCurrentStorePriceAsync(request.StoreId, request.GlobalProductId, cancellationToken);
        if (current is null)
        {
            return Result.Success();
        }

        var now = clock.UtcNow;
        current.Close(now);
        audit.Record(
            AuditActions.StorePriceRemoved,
            nameof(StoreProductPrice),
            current.Id,
            tenantId,
            previousValue: new { current.Amount, current.Currency },
            newValue: null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
