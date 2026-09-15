using YaaJuu.Modules.Inventory.Application.Contracts;
using FluentValidation;

namespace YaaJuu.Modules.Inventory.Application.Validators;

public sealed class InitializeInventoryRequestValidator : AbstractValidator<InitializeInventoryRequest>
{
    public InitializeInventoryRequestValidator()
    {
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0);
    }
}

public sealed class AdjustInventoryRequestValidator : AbstractValidator<AdjustInventoryRequest>
{
    public AdjustInventoryRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class WasteInventoryRequestValidator : AbstractValidator<WasteInventoryRequest>
{
    public WasteInventoryRequestValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
