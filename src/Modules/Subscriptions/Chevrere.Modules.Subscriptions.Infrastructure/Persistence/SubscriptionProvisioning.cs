using Chevrere.Infrastructure.Options;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Subscriptions.Application.Abstractions;
using Chevrere.Modules.Subscriptions.Domain;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Results;
using Chevrere.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Chevrere.Modules.Subscriptions.Infrastructure.Persistence;

public sealed class SubscriptionProvisioning(
    ChevrereDbContext dbContext,
    IOptions<SubscriptionOptions> options,
    IClock clock,
    IAuditRecorder audit) : ISubscriptionProvisioning
{
    public async Task<Result<Subscription>> StartAsync(
        Guid tenantId,
        string planCode,
        CancellationToken cancellationToken)
    {
        var plan = await FindPlanAsync(planCode, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<Subscription>(Error.NotFound("plan.not_found", "The specified plan does not exist."));
        }

        if (!plan.IsActive)
        {
            return Result.Failure<Subscription>(Error.Domain("plan.inactive", "Cannot subscribe to an inactive plan."));
        }

        var exists = await dbContext.Subscriptions
            .AnyAsync(s => s.TenantId == tenantId && s.Status != SubscriptionStatus.Cancelled, cancellationToken);
        if (exists)
        {
            return Result.Failure<Subscription>(
                Error.Conflict("subscription.exists", "The tenant already has an active subscription."));
        }

        if (!Enum.TryParse<SubscriptionStatus>(options.Value.DefaultInitialStatus, ignoreCase: true, out var initial)
            || initial is not (SubscriptionStatus.Trial or SubscriptionStatus.Active))
        {
            initial = SubscriptionStatus.Trial;
        }

        try
        {
            var subscription = Subscription.Start(
                tenantId,
                plan,
                initial,
                options.Value.TrialDays,
                clock.UtcNow);

            dbContext.Subscriptions.Add(subscription);
            return Result.Success(subscription);
        }
        catch (DomainException ex)
        {
            return Result.Failure<Subscription>(Error.Domain(ex.Code, ex.Message));
        }
    }

    public Task<Subscription?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        dbContext.Subscriptions
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Result> ChangePlanAsync(Guid tenantId, string planCode, CancellationToken cancellationToken)
    {
        var subscription = await GetByTenantAsync(tenantId, cancellationToken);
        if (subscription is null)
        {
            return Result.Failure(Error.NotFound(ErrorCodes.NotFound, "Subscription not found."));
        }

        var plan = await FindPlanAsync(planCode, cancellationToken);
        if (plan is null)
        {
            return Result.Failure(Error.NotFound("plan.not_found", "The specified plan does not exist."));
        }

        var previousPlanId = subscription.PlanId;
        try
        {
            subscription.ChangePlan(plan, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict(ex.Code, ex.Message));
        }

        audit.Record(
            AuditActions.PlanChanged,
            nameof(Subscription),
            subscription.Id,
            tenantId,
            previousValue: new { PlanId = previousPlanId },
            newValue: new { PlanId = plan.Id, PlanCode = plan.Code });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private Task<Plan?> FindPlanAsync(string planCode, CancellationToken cancellationToken)
    {
        var normalized = planCode.Trim().ToUpperInvariant();
        return dbContext.Plans.SingleOrDefaultAsync(p => p.Code == normalized, cancellationToken);
    }
}
