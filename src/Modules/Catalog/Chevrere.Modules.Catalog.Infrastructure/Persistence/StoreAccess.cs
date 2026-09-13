using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Catalog.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Chevrere.Modules.Catalog.Infrastructure.Persistence;

public sealed class StoreAccess(ChevrereDbContext dbContext) : IStoreAccess
{
    public async Task<Guid?> GetStoreTenantIdAsync(Guid storeId, CancellationToken cancellationToken)
    {
        return await dbContext.Stores.AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => (Guid?)s.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
