using YaaJuu.Modules.Subscriptions.Application.Contracts;

namespace YaaJuu.Modules.Subscriptions.Application.Abstractions;

public interface ISubscriptionReader
{
    Task<SubscriptionDto?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}
