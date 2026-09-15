using YaaJuu.Modules.Identity.Application.Contracts;
using YaaJuu.SharedKernel.Results;

namespace YaaJuu.Modules.Identity.Application.Abstractions;

public interface IUserAuthenticator
{
    Task<Result<LoginResponse>> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken);
}
