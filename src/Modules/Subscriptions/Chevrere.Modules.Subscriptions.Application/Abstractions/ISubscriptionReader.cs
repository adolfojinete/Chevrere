using Chevrere.Modules.Subscriptions.Application.Contracts;

namespace Chevrere.Modules.Subscriptions.Application.Abstractions;

public interface ISubscriptionReader
{
    Task<SubscriptionDto?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}
