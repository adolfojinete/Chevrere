using Chevrere.Modules.Identity.Application.Contracts;
using FluentValidation;

namespace Chevrere.Modules.Identity.Application.Login;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
