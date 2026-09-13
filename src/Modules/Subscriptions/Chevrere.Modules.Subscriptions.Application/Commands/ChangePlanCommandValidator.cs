using FluentValidation;

namespace Chevrere.Modules.Subscriptions.Application.Commands;

public sealed class ChangePlanCommandValidator : AbstractValidator<ChangePlanCommand>
{
    public ChangePlanCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.PlanCode).NotEmpty().MaximumLength(50);
    }
}
