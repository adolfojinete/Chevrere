using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Catalog.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace YaaJuu.Modules.Catalog.Infrastructure.Persistence;

public sealed class StoreAccess(YaaJuuDbContext dbContext) : IStoreAccess
{
    public async Task<Guid?> GetStoreTenantIdAsync(Guid storeId, CancellationToken cancellationToken)
    {
        return await dbContext.Stores.AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => (Guid?)s.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
