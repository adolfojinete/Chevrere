using YaaJuu.Modules.Subscriptions.Application.Abstractions;
using YaaJuu.Modules.Subscriptions.Application.Contracts;
using YaaJuu.SharedKernel.Application;

namespace YaaJuu.Modules.Subscriptions.Application.Queries;

public sealed record ListPlansQuery;

public sealed class ListPlansHandler(IPlanReader plans) : IHandler<ListPlansQuery, IReadOnlyList<PlanDto>>
{
    public Task<IReadOnlyList<PlanDto>> HandleAsync(ListPlansQuery request, CancellationToken cancellationToken)
        => plans.ListActiveAsync(cancellationToken);
}
