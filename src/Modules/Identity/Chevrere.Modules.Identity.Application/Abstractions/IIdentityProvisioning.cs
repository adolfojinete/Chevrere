using Chevrere.Modules.Identity.Application.Contracts;
using Chevrere.SharedKernel.Results;

namespace Chevrere.Modules.Identity.Application.Abstractions;

public interface IIdentityProvisioning
{
    Task<Result<OwnerUserResult>> AddOwnerAsync(OwnerUserRequest request, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);
}
