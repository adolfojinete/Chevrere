using YaaJuu.Infrastructure.Persistence;
using YaaJuu.SharedKernel.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace YaaJuu.Infrastructure.Idempotency;

public sealed class EfIdempotencyStore(YaaJuuDbContext dbContext) : IIdempotencyStore
{
    public Task<IdempotentOperation?> FindAsync(
        Guid tenantId,
        string operation,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        dbContext.IdempotentOperations.AsNoTracking().FirstOrDefaultAsync(
            o => o.TenantId == tenantId && o.Operation == operation && o.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public void Add(IdempotentOperation operation) => dbContext.IdempotentOperations.Add(operation);

    public void DiscardPending(IdempotentOperation operation)
    {
        var entry = dbContext.Entry(operation);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }
}
