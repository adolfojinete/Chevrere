using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Consumer.Application.Abstractions;
using Chevrere.Modules.Consumer.Domain.ValueObjects;
using Chevrere.Modules.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Chevrere.Modules.Consumer.Infrastructure.Persistence;

/// <summary>
/// Resolves the single dark store that serves a coordinate: enabled area, active store, active
/// tenant, nearest center first.
/// </summary>
/// <remarks>
/// <para>
/// Written as parameterised SQL on purpose. The radius is a per-row column, so the predicate is
/// <c>ST_DWithin(location, point, service_radius_meters)</c>; keeping it in SQL makes the GiST index
/// usage and the ordering obvious instead of depending on how a LINQ tree happens to translate.
/// </para>
/// <para>
/// This is also the only place allowed to run with <c>IgnoreQueryFilters</c> for anonymous callers:
/// a consumer has no tenant, so the tenant filter would match nothing. Every tenant-scoped condition
/// is therefore spelled out here.
/// </para>
/// </remarks>
public sealed class ConsumerStoreResolver(ChevrereDbContext dbContext) : IConsumerStoreResolver
{
    public async Task<ConsumerFulfillmentStore?> ResolveEligibleStoreAsync(
        GeoCoordinate location,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);

        var longitude = location.Longitude;
        var latitude = location.Latitude;
        var activeStore = nameof(StoreStatus.Active);
        var activeTenant = nameof(TenantStatus.Active);

        var areaId = await dbContext.Database
            .SqlQuery<Guid>($"""
                SELECT a.id AS "Value"
                FROM store_service_areas AS a
                INNER JOIN stores AS s ON s.id = a.store_id AND s.tenant_id = a.tenant_id
                INNER JOIN tenants AS t ON t.id = a.tenant_id
                WHERE a.is_enabled
                  AND s.status = {activeStore}
                  AND t.status = {activeTenant}
                  AND ST_DWithin(
                        a.location,
                        ST_SetSRID(ST_MakePoint({longitude}, {latitude}), 4326)::geography,
                        a.service_radius_meters)
                ORDER BY
                  ST_Distance(
                    a.location,
                    ST_SetSRID(ST_MakePoint({longitude}, {latitude}), 4326)::geography),
                  a.store_id
                LIMIT 1
                """)
            .FirstOrDefaultAsync(cancellationToken);

        if (areaId == Guid.Empty)
        {
            return null;
        }

        return await dbContext.StoreServiceAreas.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.Id == areaId)
            .Select(a => new ConsumerFulfillmentStore(a.StoreId, a.TenantId, a.Id))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
