using Chevrere.Modules.Subscriptions.Application.Abstractions;
using Chevrere.Modules.Subscriptions.Application.Contracts;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;

namespace Chevrere.Modules.Subscriptions.Application.Commands;

public sealed record ChangePlanCommand(Guid TenantId, string PlanCode);

public sealed class ChangePlanHandler(ISubscriptionProvisioning subscriptions)
    : IHandler<ChangePlanCommand, Result>
{
    public Task<Result> HandleAsync(ChangePlanCommand request, CancellationToken cancellationToken)
        => subscriptions.ChangePlanAsync(request.TenantId, request.PlanCode, cancellationToken);
}
