using YaaJuu.Modules.Tenancy.Application.Contracts;
using FluentValidation;

namespace YaaJuu.Modules.Tenancy.Application.Commands.CreateFranchisee;

public sealed class CreateFranchiseeCommandValidator : AbstractValidator<CreateFranchiseeRequest>
{
    public CreateFranchiseeCommandValidator()
    {
        RuleFor(x => x.TenantCode).NotEmpty().MinimumLength(3).MaximumLength(50);
        RuleFor(x => x.TenantName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FranchiseeCode).MaximumLength(50).When(x => !string.IsNullOrWhiteSpace(x.FranchiseeCode));
        RuleFor(x => x.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TradeName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.IdentificationType).IsInEnum();
        RuleFor(x => x.IdentificationNumber).NotEmpty().MinimumLength(5).MaximumLength(32);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Phone).NotEmpty().MinimumLength(7).MaximumLength(32);
        RuleFor(x => x.OwnerEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.OwnerDisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OwnerPassword).NotEmpty().MinimumLength(10);
        RuleFor(x => x.StoreCode).NotEmpty().MinimumLength(3).MaximumLength(50);
        RuleFor(x => x.StoreName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AddressInternal).NotEmpty().MaximumLength(500);
        RuleFor(x => x.PlanCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90)
            .When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180)
            .When(x => x.Longitude.HasValue);
        RuleFor(x => x)
            .Must(x => x.Latitude.HasValue == x.Longitude.HasValue)
            .WithMessage("Latitude and longitude must be provided together.");
    }
}
