using Chevrere.SharedKernel.Domain;

namespace Chevrere.Modules.Tenancy.Domain.ValueObjects;

public sealed class GeoCoordinate : ValueObject
{
    private GeoCoordinate(decimal latitude, decimal longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public decimal Latitude { get; }

    public decimal Longitude { get; }

    public static GeoCoordinate? Create(decimal? latitude, decimal? longitude)
    {
        if (latitude is null && longitude is null)
        {
            return null;
        }

        if (latitude is null || longitude is null)
        {
            throw new DomainException(
                "geo.incomplete",
                "Latitude and longitude must be provided together.");
        }

        if (latitude is < -90 or > 90)
        {
            throw new DomainException("geo.latitude.invalid", "Latitude must be between -90 and 90.");
        }

        if (longitude is < -180 or > 180)
        {
            throw new DomainException("geo.longitude.invalid", "Longitude must be between -180 and 180.");
        }

        return new GeoCoordinate(latitude.Value, longitude.Value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }
}
