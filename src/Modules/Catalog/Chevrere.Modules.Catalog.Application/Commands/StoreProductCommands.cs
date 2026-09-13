using Chevrere.Modules.Catalog.Application.Abstractions;
using Chevrere.Modules.Catalog.Application.Contracts;
using Chevrere.Modules.Catalog.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Results;
using Chevrere.SharedKernel.Time;

namespace Chevrere.Modules.Catalog.Application.Commands;

public sealed record EnableStoreProductCommand(Guid StoreId, Guid GlobalProductId);

public sealed record DisableStoreProductCommand(Guid StoreId, Guid GlobalProductId);

public sealed class EnableStoreProductHandler(
    ICatalogStore store,
    IStoreAccess storeAccess,
    ICurrentUser currentUser,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<EnableStoreProductCommand, Result<StoreProductDto>>
{
    public async Task<Result<StoreProductDto>> HandleAsync(
        EnableStoreProductCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<StoreProductDto>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var storeTenantId = await storeAccess.GetStoreTenantIdAsync(request.StoreId, cancellationToken);
        if (storeTenantId is null || storeTenantId != tenantId)
        {
            return Result.Failure<StoreProductDto>(Error.NotFound(ErrorCodes.NotFound, "Store not found."));
        }

        var product = await store.GetProductAsync(request.GlobalProductId, cancellationToken);
        if (product is null)
        {
            return Result.Failure<StoreProductDto>(Error.NotFound(ErrorCodes.NotFound, "Product not found."));
        }

        var category = await store.GetCategoryAsync(product.CategoryId, cancellationToken);
        if (category is null || !CommercialAvailability.IsBrowsable(category, product))
        {
            return Result.Failure<StoreProductDto>(
                Error.Conflict("product.not_available", "The product is not commercially available to enable."));
        }

        try
        {
            var existing = await store.GetStoreProductAsync(request.StoreId, product.Id, cancellationToken);
            var created = false;

            if (existing is null)
            {
                existing = StoreProduct.EnableForStore(tenantId, request.StoreId, product, clock.UtcNow);
                store.AddStoreProduct(existing);
                created = true;
            }
            else if (!existing.Enable(product, clock.UtcNow))
            {
                return Result.Success(ToDto(existing, product, category));
            }

            audit.Record(
                AuditActions.StoreProductEnabled,
                nameof(StoreProduct),
                existing.Id,
                tenantId,
                previousValue: created ? null : new { IsEnabled = false },
                newValue: created
                    ? new { existing.StoreId, existing.GlobalProductId, IsEnabled = true }
                    : new { IsEnabled = true });

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DuplicateKeyException)
            {
                var winner = await store.GetStoreProductAsync(request.StoreId, product.Id, cancellationToken);
                if (winner is null)
                {
                    throw;
                }

                return Result.Success(ToDto(winner, product, category));
            }

            return Result.Success(ToDto(existing, product, category));
        }
        catch (DomainException ex)
        {
            return Result.Failure<StoreProductDto>(Error.Domain(ex.Code, ex.Message));
        }
    }

    private static StoreProductDto ToDto(StoreProduct offering, GlobalProduct product, Category category) =>
        new(
            offering.Id,
            offering.StoreId,
            offering.GlobalProductId,
            product.Sku,
            product.Name,
            product.Brand,
            product.Presentation,
            category.Name,
            offering.IsEnabled,
            CommercialAvailability.IsAvailable(category, product, offering),
            offering.UpdatedAt);
}

public sealed class DisableStoreProductHandler(
    ICatalogStore store,
    IStoreAccess storeAccess,
    ICurrentUser currentUser,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<DisableStoreProductCommand, Result>
{
    public async Task<Result> HandleAsync(DisableStoreProductCommand request, CancellationToken cancellationToken)
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

        var existing = await store.GetStoreProductAsync(request.StoreId, request.GlobalProductId, cancellationToken);
        if (existing is null || !existing.BelongsTo(tenantId, request.StoreId))
        {
            return Result.Failure(Error.NotFound(ErrorCodes.NotFound, "Store product not found."));
        }

        if (!existing.Disable(clock.UtcNow))
        {
            return Result.Success();
        }

        audit.Record(
            AuditActions.StoreProductDisabled,
            nameof(StoreProduct),
            existing.Id,
            tenantId,
            previousValue: new { IsEnabled = true },
            newValue: new { IsEnabled = false });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
