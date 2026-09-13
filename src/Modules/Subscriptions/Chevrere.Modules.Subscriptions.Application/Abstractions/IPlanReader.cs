using Chevrere.Modules.Subscriptions.Application.Contracts;

namespace Chevrere.Modules.Subscriptions.Application.Abstractions;

public interface IPlanReader
{
    Task<IReadOnlyList<PlanDto>> ListActiveAsync(CancellationToken cancellationToken);

    Task<PlanDto?> GetByCodeAsync(string code, CancellationToken cancellationToken);
}
