using Chevrere.Modules.Catalog.Application.Contracts;
using FluentValidation;

namespace Chevrere.Modules.Catalog.Application.Validators;

public sealed class CreateCategoryValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MinimumLength(2).MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Description).MaximumLength(300).When(x => x.Description is not null);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateCategoryValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Description).MaximumLength(300).When(x => x.Description is not null);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateGlobalProductValidator : AbstractValidator<CreateGlobalProductRequest>
{
    public CreateGlobalProductValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Sku).NotEmpty().MinimumLength(3).MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Brand).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Presentation).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
        RuleFor(x => x.Barcode).MaximumLength(64).When(x => !string.IsNullOrWhiteSpace(x.Barcode));
    }
}

public sealed class UpdateGlobalProductValidator : AbstractValidator<UpdateGlobalProductRequest>
{
    public UpdateGlobalProductValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Brand).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Presentation).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
        RuleFor(x => x.Barcode).MaximumLength(64).When(x => !string.IsNullOrWhiteSpace(x.Barcode));
    }
}
