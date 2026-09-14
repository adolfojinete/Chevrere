using Chevrere.Modules.Consumer.Domain.ValueObjects;
using Chevrere.SharedKernel.Domain;

namespace Chevrere.Modules.Consumer.Domain;

/// <summary>
/// Where a dark store delivers: a center plus a radius. Deliberately separate from the Store's own
/// address and coordinates, which are operational data and never reach a consumer response.
/// </summary>
public sealed class StoreServiceArea : AggregateRoot
{
    private StoreServiceArea()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public int ServiceRadiusMeters { get; private set; }

    public bool IsEnabled { get; private set; }

    /// <summary>
    /// First configuration. The area is born disabled: drawing it on a map is not the same decision
    /// as opening the store to consumers.
    /// </summary>
    public static StoreServiceArea Create(
        Guid tenantId,
        Guid storeId,
        GeoCoordinate center,
        ServiceRadius radius,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentNullException.ThrowIfNull(radius);

        if (tenantId == Guid.Empty)
        {
            throw new DomainException("service_area.tenant.required", "A service area must belong to a tenant.");
        }

        if (storeId == Guid.Empty)
        {
            throw new DomainException("service_area.store.required", "A service area must belong to a store.");
        }

        return new StoreServiceArea
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            StoreId = storeId,
            Latitude = center.Latitude,
            Longitude = center.Longitude,
            ServiceRadiusMeters = radius.Meters,
            IsEnabled = false,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    /// <returns>true when center or radius actually changed.</returns>
    public bool Configure(GeoCoordinate center, ServiceRadius radius, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentNullException.ThrowIfNull(radius);

        if (Latitude.Equals(center.Latitude)
            && Longitude.Equals(center.Longitude)
            && ServiceRadiusMeters == radius.Meters)
        {
            return false;
        }

        Latitude = center.Latitude;
        Longitude = center.Longitude;
        ServiceRadiusMeters = radius.Meters;
        UpdatedAt = utcNow;
        return true;
    }

    /// <returns>true when IsEnabled actually changed to true.</returns>
    public bool Enable(DateTimeOffset utcNow)
    {
        if (IsEnabled)
        {
            return false;
        }

        IsEnabled = true;
        UpdatedAt = utcNow;
        return true;
    }

    /// <returns>true when IsEnabled actually changed to false.</returns>
    public bool Disable(DateTimeOffset utcNow)
    {
        if (!IsEnabled)
        {
            return false;
        }

        IsEnabled = false;
        UpdatedAt = utcNow;
        return true;
    }

    public bool BelongsTo(Guid tenantId) => TenantId == tenantId;

    /// <summary>
    /// Enabling or disabling a store that was never drawn on the map is not a conflict, it is a
    /// missing resource. The API turns this into a 404.
    /// </summary>
    public static DomainException NotConfigured() => new(
        "service_area.not_configured",
        "The store does not have a service area configured.");
}
