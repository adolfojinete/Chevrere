using Chevrere.Modules.Identity.Application.Contracts;
using Chevrere.SharedKernel.Results;

namespace Chevrere.Modules.Identity.Application.Abstractions;

public interface IUserAuthenticator
{
    Task<Result<LoginResponse>> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken);
}
