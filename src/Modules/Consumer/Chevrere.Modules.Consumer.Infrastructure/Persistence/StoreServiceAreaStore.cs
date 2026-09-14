using Chevrere.Infrastructure.Persistence;
using Chevrere.Infrastructure.Persistence.Configurations;
using Chevrere.Modules.Consumer.Application.Abstractions;
using Chevrere.Modules.Consumer.Domain;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Chevrere.Modules.Consumer.Infrastructure.Persistence;

public sealed class StoreServiceAreaStore(ChevrereDbContext dbContext) : IStoreServiceAreaStore
{
    public Task<StoreServiceArea?> GetByStoreAsync(Guid storeId, CancellationToken cancellationToken) =>
        dbContext.StoreServiceAreas.FirstOrDefaultAsync(a => a.StoreId == storeId, cancellationToken);

    public void Upsert(StoreServiceArea area)
    {
        ArgumentNullException.ThrowIfNull(area);

        var entry = dbContext.Entry(area);
        if (entry.State == EntityState.Detached)
        {
            dbContext.StoreServiceAreas.Add(area);
            entry = dbContext.Entry(area);
        }

        // PostGIS point order is (X, Y) = (longitude, latitude). Swapping them silently moves every
        // Bogotá store into the Indian Ocean, so this is the single place that builds the geometry.
        entry.Property<Point>(StoreServiceAreaConfiguration.LocationProperty).CurrentValue =
            new Point(area.Longitude, area.Latitude) { SRID = 4326 };
    }
}

public sealed class ConsumerStoreAccess(ChevrereDbContext dbContext) : IConsumerStoreAccess
{
    public async Task<Guid?> GetStoreTenantIdAsync(Guid storeId, CancellationToken cancellationToken) =>
        await dbContext.Stores.AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => (Guid?)s.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
}
