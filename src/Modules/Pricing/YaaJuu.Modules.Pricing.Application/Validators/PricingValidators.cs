using YaaJuu.Modules.Pricing.Application.Contracts;
using YaaJuu.SharedKernel.Domain.ValueObjects;
using FluentValidation;

namespace YaaJuu.Modules.Pricing.Application.Validators;

public sealed class SetPriceRequestValidator : AbstractValidator<SetPriceRequest>
{
    public SetPriceRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency)
            .NotEmpty()
            .Must(c => !string.IsNullOrWhiteSpace(c) && c.Trim().Length == 3)
            .WithMessage("Currency must be a 3-letter ISO code.")
            .Must(c => CurrencyCodes.IsSupported(c.Trim().ToUpperInvariant()))
            .WithMessage($"Currency must be one of: {CurrencyCodes.Cop}.");
    }
}
