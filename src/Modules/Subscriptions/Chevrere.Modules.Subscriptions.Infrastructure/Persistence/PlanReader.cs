using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Subscriptions.Application.Abstractions;
using Chevrere.Modules.Subscriptions.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Chevrere.Modules.Subscriptions.Infrastructure.Persistence;

public sealed class PlanReader(ChevrereDbContext dbContext) : IPlanReader
{
    public async Task<IReadOnlyList<PlanDto>> ListActiveAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Plans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Code)
            .Select(p => new PlanDto(
                p.Id,
                p.Code,
                p.Name,
                p.Description,
                p.MonthlyPrice,
                p.Currency,
                p.BillingPeriod,
                p.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<PlanDto?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await dbContext.Plans
            .AsNoTracking()
            .Where(p => p.Code == normalized)
            .Select(p => new PlanDto(
                p.Id,
                p.Code,
                p.Name,
                p.Description,
                p.MonthlyPrice,
                p.Currency,
                p.BillingPeriod,
                p.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
