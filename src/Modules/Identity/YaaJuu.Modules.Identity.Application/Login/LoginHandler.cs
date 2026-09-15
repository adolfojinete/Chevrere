using YaaJuu.Modules.Identity.Application.Abstractions;
using YaaJuu.Modules.Identity.Application.Contracts;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;

namespace YaaJuu.Modules.Identity.Application.Login;

public sealed class LoginHandler(IUserAuthenticator authenticator)
    : IHandler<LoginRequest, Result<LoginResponse>>
{
    public Task<Result<LoginResponse>> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
        => authenticator.AuthenticateAsync(request, cancellationToken);
}
