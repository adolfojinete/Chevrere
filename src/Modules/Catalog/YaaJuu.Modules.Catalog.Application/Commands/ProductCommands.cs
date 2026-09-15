using YaaJuu.Modules.Catalog.Application.Abstractions;
using YaaJuu.Modules.Catalog.Application.Contracts;
using YaaJuu.Modules.Catalog.Domain;
using YaaJuu.Modules.Catalog.Domain.ValueObjects;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Domain;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using YaaJuu.SharedKernel.Time;

namespace YaaJuu.Modules.Catalog.Application.Commands;

public sealed class CreateGlobalProductHandler(
    ICatalogStore store,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<CreateGlobalProductRequest, Result<GlobalProductDto>>
{
    public async Task<Result<GlobalProductDto>> HandleAsync(
        CreateGlobalProductRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var category = await store.GetCategoryAsync(request.CategoryId, cancellationToken);
            if (category is null)
            {
                return Result.Failure<GlobalProductDto>(Error.NotFound("category.not_found", "Category not found."));
            }

            var sku = Sku.Create(request.Sku);
            var slug = await UniqueProductSlugAsync(store, request.Name, cancellationToken);
            var barcode = Barcode.Create(request.Barcode);

            if (await store.ProductSkuExistsAsync(sku.Value, cancellationToken))
            {
                return Result.Failure<GlobalProductDto>(Error.Conflict("product.sku.duplicate", "The SKU is already in use."));
            }

            if (barcode is not null && await store.ProductBarcodeExistsAsync(barcode.Value, null, cancellationToken))
            {
                return Result.Failure<GlobalProductDto>(Error.Conflict("product.barcode.duplicate", "The barcode is already in use."));
            }

            var product = GlobalProduct.Create(
                category,
                sku,
                request.Name,
                slug,
                request.Brand,
                request.Presentation,
                request.Description,
                barcode,
                clock.UtcNow);

            store.AddProduct(product);
            audit.Record(
                AuditActions.GlobalProductCreated,
                nameof(GlobalProduct),
                product.Id,
                null,
                newValue: new { product.Sku, product.Name, product.CategoryId });
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(ToDto(product, category.Name));
        }
        catch (DomainException ex)
        {
            return Result.Failure<GlobalProductDto>(Error.Domain(ex.Code, ex.Message));
        }
    }

    internal static async Task<Slug> UniqueProductSlugAsync(
        ICatalogStore store,
        string name,
        CancellationToken cancellationToken)
    {
        var baseSlug = Slug.FromName(name);
        if (!await store.ProductSlugExistsAsync(baseSlug.Value, cancellationToken))
        {
            return baseSlug;
        }

        for (var suffix = 2; suffix < 100; suffix++)
        {
            var candidate = Slug.Create($"{baseSlug.Value}-{suffix}");
            if (!await store.ProductSlugExistsAsync(candidate.Value, cancellationToken))
            {
                return candidate;
            }
        }

        return Slug.Create($"{baseSlug.Value}-{Guid.CreateVersion7():N}"[..Slug.MaxLength]);
    }

    internal static GlobalProductDto ToDto(GlobalProduct product, string categoryName) =>
        new(
            product.Id,
            product.CategoryId,
            categoryName,
            product.Sku,
            product.Name,
            product.Slug,
            product.Brand,
            product.Presentation,
            product.Description,
            product.Barcode,
            product.Status,
            product.CreatedAt,
            product.UpdatedAt);
}

public sealed record UpdateGlobalProductCommand(Guid Id, UpdateGlobalProductRequest Request);

public sealed class UpdateGlobalProductHandler(ICatalogStore store, IAuditRecorder audit, IUnitOfWork unitOfWork, IClock clock)
    : IHandler<UpdateGlobalProductCommand, Result<GlobalProductDto>>
{
    public async Task<Result<GlobalProductDto>> HandleAsync(
        UpdateGlobalProductCommand request,
        CancellationToken cancellationToken)
    {
        var product = await store.GetProductAsync(request.Id, cancellationToken);
        if (product is null)
        {
            return Result.Failure<GlobalProductDto>(Error.NotFound(ErrorCodes.NotFound, "Product not found."));
        }

        var category = await store.GetCategoryAsync(request.Request.CategoryId, cancellationToken);
        if (category is null)
        {
            return Result.Failure<GlobalProductDto>(Error.NotFound("category.not_found", "Category not found."));
        }

        try
        {
            var barcode = Barcode.Create(request.Request.Barcode);
            if (barcode is not null &&
                await store.ProductBarcodeExistsAsync(barcode.Value, product.Id, cancellationToken))
            {
                return Result.Failure<GlobalProductDto>(Error.Conflict("product.barcode.duplicate", "The barcode is already in use."));
            }

            var previous = new { product.Name, product.Brand, product.Presentation, product.CategoryId, product.Barcode };
            product.Update(
                category,
                request.Request.Name,
                request.Request.Brand,
                request.Request.Presentation,
                request.Request.Description,
                barcode,
                clock.UtcNow);

            audit.Record(
                AuditActions.GlobalProductUpdated,
                nameof(GlobalProduct),
                product.Id,
                null,
                previousValue: previous,
                newValue: new { product.Name, product.Brand, product.Presentation, product.CategoryId, product.Barcode });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(CreateGlobalProductHandler.ToDto(product, category.Name));
        }
        catch (DomainException ex)
        {
            return Result.Failure<GlobalProductDto>(Error.Domain(ex.Code, ex.Message));
        }
    }
}

public sealed record ActivateGlobalProductCommand(Guid Id);

public sealed class ActivateGlobalProductHandler(ICatalogStore store, IAuditRecorder audit, IUnitOfWork unitOfWork, IClock clock)
    : IHandler<ActivateGlobalProductCommand, Result>
{
    public async Task<Result> HandleAsync(ActivateGlobalProductCommand request, CancellationToken cancellationToken)
    {
        var product = await store.GetProductAsync(request.Id, cancellationToken);
        if (product is null)
        {
            return Result.Failure(Error.NotFound(ErrorCodes.NotFound, "Product not found."));
        }

        var previous = product.Status;
        try
        {
            if (!product.Activate(clock.UtcNow))
            {
                return Result.Success();
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict(ex.Code, ex.Message));
        }

        audit.Record(AuditActions.GlobalProductActivated, nameof(GlobalProduct), product.Id, null, previousValue: new { Status = previous }, newValue: new { product.Status });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record DeactivateGlobalProductCommand(Guid Id);

public sealed class DeactivateGlobalProductHandler(ICatalogStore store, IAuditRecorder audit, IUnitOfWork unitOfWork, IClock clock)
    : IHandler<DeactivateGlobalProductCommand, Result>
{
    public async Task<Result> HandleAsync(DeactivateGlobalProductCommand request, CancellationToken cancellationToken)
    {
        var product = await store.GetProductAsync(request.Id, cancellationToken);
        if (product is null)
        {
            return Result.Failure(Error.NotFound(ErrorCodes.NotFound, "Product not found."));
        }

        var previous = product.Status;
        try
        {
            if (!product.Deactivate(clock.UtcNow))
            {
                return Result.Success();
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict(ex.Code, ex.Message));
        }

        audit.Record(AuditActions.GlobalProductDeactivated, nameof(GlobalProduct), product.Id, null, previousValue: new { Status = previous }, newValue: new { product.Status });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
