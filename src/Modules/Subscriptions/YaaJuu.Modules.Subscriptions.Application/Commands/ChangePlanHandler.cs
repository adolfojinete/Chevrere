using YaaJuu.Modules.Subscriptions.Application.Abstractions;
using YaaJuu.Modules.Subscriptions.Application.Contracts;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;

namespace YaaJuu.Modules.Subscriptions.Application.Commands;

public sealed record ChangePlanCommand(Guid TenantId, string PlanCode);

public sealed class ChangePlanHandler(ISubscriptionProvisioning subscriptions)
    : IHandler<ChangePlanCommand, Result>
{
    public Task<Result> HandleAsync(ChangePlanCommand request, CancellationToken cancellationToken)
        => subscriptions.ChangePlanAsync(request.TenantId, request.PlanCode, cancellationToken);
}
