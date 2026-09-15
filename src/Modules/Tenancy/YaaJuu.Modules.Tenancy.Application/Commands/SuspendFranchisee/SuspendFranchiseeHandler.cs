using YaaJuu.Modules.Subscriptions.Application.Abstractions;
using YaaJuu.Modules.Tenancy.Application.Abstractions;
using YaaJuu.Modules.Tenancy.Domain;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Domain;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using YaaJuu.SharedKernel.Time;

namespace YaaJuu.Modules.Tenancy.Application.Commands.SuspendFranchisee;

public sealed record SuspendFranchiseeCommand(Guid FranchiseeId, string Reason);

public sealed class SuspendFranchiseeHandler(
    ITenancyStore store,
    ISubscriptionProvisioning subscriptions,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<SuspendFranchiseeCommand, Result>
{
    public async Task<Result> HandleAsync(SuspendFranchiseeCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result.Failure(Error.Validation("suspension.reason.required", "A suspension reason is required."));
        }

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

        var previousFranchisee = franchisee.Status;
        var previousTenant = tenant.Status;
        var now = clock.UtcNow;

        try
        {
            franchisee.Suspend(now);
            tenant.Suspend(now);

            var subscription = await subscriptions.GetByTenantAsync(franchisee.TenantId, cancellationToken);
            if (subscription is not null &&
                subscription.Status is not YaaJuu.Modules.Subscriptions.Domain.SubscriptionStatus.Cancelled
                and not YaaJuu.Modules.Subscriptions.Domain.SubscriptionStatus.Suspended)
            {
                subscription.Suspend(now, request.Reason);
                audit.Record(
                    AuditActions.SubscriptionSuspended,
                    "Subscription",
                    subscription.Id,
                    franchisee.TenantId,
                    previousValue: new { Status = previousFranchisee },
                    newValue: new { Status = subscription.Status, request.Reason });
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict(ex.Code, ex.Message));
        }

        audit.Record(
            AuditActions.FranchiseeSuspended,
            nameof(Franchisee),
            franchisee.Id,
            franchisee.TenantId,
            previousValue: new { Status = previousFranchisee, TenantStatus = previousTenant },
            newValue: new { Status = franchisee.Status, TenantStatus = tenant.Status, request.Reason });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
