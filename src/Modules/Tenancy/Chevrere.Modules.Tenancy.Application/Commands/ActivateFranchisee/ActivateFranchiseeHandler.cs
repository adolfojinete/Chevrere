using Chevrere.Modules.Tenancy.Application.Abstractions;
using Chevrere.Modules.Tenancy.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Results;
using Chevrere.SharedKernel.Time;

namespace Chevrere.Modules.Tenancy.Application.Commands.ActivateFranchisee;

public sealed record ActivateFranchiseeCommand(Guid FranchiseeId);

public sealed class ActivateFranchiseeHandler(
    ITenancyStore store,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<ActivateFranchiseeCommand, Result>
{
    public async Task<Result> HandleAsync(ActivateFranchiseeCommand request, CancellationToken cancellationToken)
    {
        var franchisee = await store.GetFranchiseeAsync(request.FranchiseeId, cancellationToken);
        if (franchisee is null)
        {
            return Result.Failure(Error.NotFound(ErrorCodes.NotFound, "Franchisee not found."));
        }

        var tenant = await store.GetTenantAsync(franchisee.TenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure(Error.NotFound(ErrorCodes.NotFound, "Tenant not found."));
        }

        var previous = franchisee.Status;
        var now = clock.UtcNow;

        try
        {
            franchisee.Activate(now);
            tenant.Activate(now);

            var stores = await store.ListStoresByFranchiseeAsync(franchisee.Id, cancellationToken);
            foreach (var darkStore in stores)
            {
                darkStore.Activate(now);
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict(ex.Code, ex.Message));
        }

        audit.Record(
            AuditActions.FranchiseeActivated,
            nameof(Franchisee),
            franchisee.Id,
            franchisee.TenantId,
            previousValue: new { Status = previous },
            newValue: new { Status = franchisee.Status });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
