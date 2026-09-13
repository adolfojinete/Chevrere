using Chevrere.Modules.Subscriptions.Application.Abstractions;
using Chevrere.Modules.Subscriptions.Application.Contracts;
using Chevrere.SharedKernel.Application;

namespace Chevrere.Modules.Subscriptions.Application.Queries;

public sealed record ListPlansQuery;

public sealed class ListPlansHandler(IPlanReader plans) : IHandler<ListPlansQuery, IReadOnlyList<PlanDto>>
{
    public Task<IReadOnlyList<PlanDto>> HandleAsync(ListPlansQuery request, CancellationToken cancellationToken)
        => plans.ListActiveAsync(cancellationToken);
}
