using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.Modules.Consumer.Domain.ValueObjects;

/// <summary>
/// A WGS84 point expressed in degrees. Doubles, not decimals: the distance math lives in PostGIS and
/// consumer coordinates are an approximation of where a phone is, not an accounting figure.
/// </summary>
public sealed class GeoCoordinate : ValueObject
{
    private GeoCoordinate(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }

    public static GeoCoordinate Create(double latitude, double longitude)
    {
        EnsureFinite(latitude, "geo.latitude.invalid", "Latitude");
        EnsureFinite(longitude, "geo.longitude.invalid", "Longitude");

        if (latitude is < -90 or > 90)
        {
            throw new DomainException("geo.latitude.invalid", "Latitude must be between -90 and 90.");
        }

        if (longitude is < -180 or > 180)
        {
            throw new DomainException("geo.longitude.invalid", "Longitude must be between -180 and 180.");
        }

        return new GeoCoordinate(latitude, longitude);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }

    private static void EnsureFinite(double value, string code, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new DomainException(code, $"{name} must be a finite number.");
        }
    }
}
