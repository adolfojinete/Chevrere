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

public sealed class CreateCategoryHandler(
    ICatalogStore store,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<CreateCategoryRequest, Result<CategoryDto>>
{
    public async Task<Result<CategoryDto>> HandleAsync(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var code = CategoryCode.Create(request.Code);
            var slug = Slug.FromName(request.Name);
            if (await store.CategoryCodeExistsAsync(code.Value, cancellationToken))
            {
                return Result.Failure<CategoryDto>(Error.Conflict("category.code.duplicate", "The category code is already in use."));
            }

            if (await store.CategorySlugExistsAsync(slug.Value, cancellationToken))
            {
                return Result.Failure<CategoryDto>(Error.Conflict("category.slug.duplicate", "The category slug is already in use."));
            }

            var category = Category.Create(code, request.Name, slug, request.Description, request.SortOrder, clock.UtcNow);
            store.AddCategory(category);
            audit.Record(AuditActions.CategoryCreated, nameof(Category), category.Id, null, newValue: new { category.Code, category.Name });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(category));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CategoryDto>(Error.Domain(ex.Code, ex.Message));
        }
    }

    internal static CategoryDto ToDto(Category category) =>
        new(
            category.Id,
            category.Code,
            category.Name,
            category.Slug,
            category.Description,
            category.Status,
            category.SortOrder,
            category.CreatedAt,
            category.UpdatedAt);
}

public sealed record UpdateCategoryCommand(Guid Id, UpdateCategoryRequest Request);

public sealed class UpdateCategoryHandler(ICatalogStore store, IAuditRecorder audit, IUnitOfWork unitOfWork, IClock clock)
    : IHandler<UpdateCategoryCommand, Result<CategoryDto>>
{
    public async Task<Result<CategoryDto>> HandleAsync(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await store.GetCategoryAsync(request.Id, cancellationToken);
        if (category is null)
        {
            return Result.Failure<CategoryDto>(Error.NotFound(ErrorCodes.NotFound, "Category not found."));
        }

        var previous = new { category.Name, category.Description, category.SortOrder };
        try
        {
            category.Update(request.Request.Name, request.Request.Description, request.Request.SortOrder, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure<CategoryDto>(Error.Domain(ex.Code, ex.Message));
        }

        audit.Record(
            AuditActions.CategoryUpdated,
            nameof(Category),
            category.Id,
            null,
            previousValue: previous,
            newValue: new { category.Name, category.Description, category.SortOrder });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(CreateCategoryHandler.ToDto(category));
    }
}

public sealed record ActivateCategoryCommand(Guid Id);

public sealed class ActivateCategoryHandler(ICatalogStore store, IAuditRecorder audit, IUnitOfWork unitOfWork, IClock clock)
    : IHandler<ActivateCategoryCommand, Result>
{
    public async Task<Result> HandleAsync(ActivateCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await store.GetCategoryAsync(request.Id, cancellationToken);
        if (category is null)
        {
            return Result.Failure(Error.NotFound(ErrorCodes.NotFound, "Category not found."));
        }

        var previous = category.Status;
        if (!category.Activate(clock.UtcNow))
        {
            return Result.Success();
        }

        audit.Record(
            AuditActions.CategoryActivated,
            nameof(Category),
            category.Id,
            null,
            previousValue: new { Status = previous },
            newValue: new { category.Status });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record DeactivateCategoryCommand(Guid Id);

public sealed class DeactivateCategoryHandler(ICatalogStore store, IAuditRecorder audit, IUnitOfWork unitOfWork, IClock clock)
    : IHandler<DeactivateCategoryCommand, Result>
{
    public async Task<Result> HandleAsync(DeactivateCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await store.GetCategoryAsync(request.Id, cancellationToken);
        if (category is null)
        {
            return Result.Failure(Error.NotFound(ErrorCodes.NotFound, "Category not found."));
        }

        var previous = category.Status;
        if (!category.Deactivate(clock.UtcNow))
        {
            return Result.Success();
        }

        audit.Record(
            AuditActions.CategoryDeactivated,
            nameof(Category),
            category.Id,
            null,
            previousValue: new { Status = previous },
            newValue: new { category.Status });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
