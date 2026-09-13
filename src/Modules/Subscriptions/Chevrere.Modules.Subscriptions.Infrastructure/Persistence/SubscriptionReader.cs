using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Subscriptions.Application.Abstractions;
using Chevrere.Modules.Subscriptions.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Chevrere.Modules.Subscriptions.Infrastructure.Persistence;

public sealed class SubscriptionReader(ChevrereDbContext dbContext) : ISubscriptionReader
{
    public async Task<SubscriptionDto?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return await (
                from subscription in dbContext.Subscriptions.AsNoTracking()
                join plan in dbContext.Plans.AsNoTracking() on subscription.PlanId equals plan.Id
                where subscription.TenantId == tenantId
                orderby subscription.CreatedAt descending
                select new SubscriptionDto(
                    subscription.Id,
                    subscription.TenantId,
                    subscription.PlanId,
                    plan.Code,
                    subscription.Status,
                    subscription.StartDate,
                    subscription.CurrentPeriodStart,
                    subscription.CurrentPeriodEnd,
                    subscription.NextBillingDate,
                    subscription.GracePeriodUntil,
                    subscription.SuspendedAt,
                    subscription.SuspensionReason))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
