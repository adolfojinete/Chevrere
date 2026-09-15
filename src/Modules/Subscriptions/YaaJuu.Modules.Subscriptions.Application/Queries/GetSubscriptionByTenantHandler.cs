using YaaJuu.Modules.Subscriptions.Application.Abstractions;
using YaaJuu.Modules.Subscriptions.Application.Contracts;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;

namespace YaaJuu.Modules.Subscriptions.Application.Queries;

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
