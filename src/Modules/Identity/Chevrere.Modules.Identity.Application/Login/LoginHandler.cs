using Chevrere.Modules.Identity.Application.Abstractions;
using Chevrere.Modules.Identity.Application.Contracts;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;

namespace Chevrere.Modules.Identity.Application.Login;

public sealed class LoginHandler(IUserAuthenticator authenticator)
    : IHandler<LoginRequest, Result<LoginResponse>>
{
    public Task<Result<LoginResponse>> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
        => authenticator.AuthenticateAsync(request, cancellationToken);
}
