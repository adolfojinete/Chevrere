using Chevrere.SharedKernel.Domain;

namespace Chevrere.Modules.Consumer.Domain.ValueObjects;

/// <summary>
/// Delivery reach of a dark store, in meters.
/// </summary>
/// <remarks>
/// Bounds are a product decision, not a physical limit: below 100 m the area covers little more than
/// the store's own block and is almost certainly a data entry mistake; above 50 km no quick-commerce
/// promise survives Bogotá traffic. Both ends are also enforced by a CHECK constraint.
/// </remarks>
public sealed class ServiceRadius : ValueObject
{
    public const int MinMeters = 100;

    public const int MaxMeters = 50_000;

    private ServiceRadius(int meters) => Meters = meters;

    public int Meters { get; }

    public static ServiceRadius Create(int meters)
    {
        if (meters is < MinMeters or > MaxMeters)
        {
            throw new DomainException(
                "service_area.radius.invalid",
                $"The service radius must be between {MinMeters} and {MaxMeters} meters.");
        }

        return new ServiceRadius(meters);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Meters;
    }
}
