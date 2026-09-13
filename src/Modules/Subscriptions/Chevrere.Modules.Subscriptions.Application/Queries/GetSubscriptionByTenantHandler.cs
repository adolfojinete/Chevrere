using Chevrere.Modules.Subscriptions.Application.Abstractions;
using Chevrere.Modules.Subscriptions.Application.Contracts;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;

namespace Chevrere.Modules.Subscriptions.Application.Queries;

public sealed record GetSubscriptionByTenantQuery(Guid TenantId);

public sealed class GetSubscriptionByTenantHandler(ISubscriptionReader subscriptions)
    : IHandler<GetSubscriptionByTenantQuery, Result<SubscriptionDto>>
{
    public async Task<Result<SubscriptionDto>> HandleAsync(
        GetSubscriptionByTenantQuery request,
        CancellationToken cancellationToken)
    {
        var subscription = await subscriptions.GetByTenantAsync(request.TenantId, cancellationToken);
        return subscription is null
            ? Result.Failure<SubscriptionDto>(Error.NotFound(ErrorCodes.NotFound, "Subscription not found."))
            : Result.Success(subscription);
    }
}
