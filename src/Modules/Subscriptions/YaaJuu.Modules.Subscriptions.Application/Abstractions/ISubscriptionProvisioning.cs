using YaaJuu.Modules.Subscriptions.Domain;
using YaaJuu.SharedKernel.Results;

namespace YaaJuu.Modules.Subscriptions.Application.Abstractions;

public interface ISubscriptionProvisioning
{
    Task<Result<Subscription>> StartAsync(
        Guid tenantId,
        string planCode,
        CancellationToken cancellationToken);

    Task<Subscription?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<Result> ChangePlanAsync(Guid tenantId, string planCode, CancellationToken cancellationToken);
}
