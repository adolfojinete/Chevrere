using Chevrere.Modules.Subscriptions.Application.Abstractions;
using Chevrere.Modules.Subscriptions.Domain;
using Chevrere.Modules.Tenancy.Application.Abstractions;
using Chevrere.Modules.Tenancy.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Results;
using Chevrere.SharedKernel.Time;

namespace Chevrere.Modules.Tenancy.Application.Commands.ReactivateFranchisee;

public sealed record ReactivateFranchiseeCommand(Guid FranchiseeId);

public sealed class ReactivateFranchiseeHandler(
    ITenancyStore store,
    ISubscriptionProvisioning subscriptions,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<ReactivateFranchiseeCommand, Result>
{
    public async Task<Result> HandleAsync(ReactivateFranchiseeCommand request, CancellationToken cancellationToken)
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
            franchisee.Reactivate(now);
            tenant.Activate(now);

            var subscription = await subscriptions.GetByTenantAsync(franchisee.TenantId, cancellationToken);
            if (subscription?.Status is SubscriptionStatus.Suspended)
            {
                subscription.Reactivate(now);
                audit.Record(
                    AuditActions.SubscriptionReactivated,
                    "Subscription",
                    subscription.Id,
                    franchisee.TenantId,
                    previousValue: new { Status = SubscriptionStatus.Suspended },
                    newValue: new { Status = subscription.Status });
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict(ex.Code, ex.Message));
        }

        audit.Record(
            AuditActions.FranchiseeReactivated,
            nameof(Franchisee),
            franchisee.Id,
            franchisee.TenantId,
            previousValue: new { Status = previous },
            newValue: new { Status = franchisee.Status });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
