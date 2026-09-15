using YaaJuu.Modules.Consumer.Domain;
using YaaJuu.Modules.Consumer.Domain.ValueObjects;
using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.UnitTests.Consumer;

public sealed class GeoCoordinateTests
{
    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(4.65d, -74.06d)]
    [InlineData(-90d, -180d)]
    [InlineData(90d, 180d)]
    public void Accepts_coordinates_inside_the_wgs84_range(double latitude, double longitude)
    {
        var coordinate = GeoCoordinate.Create(latitude, longitude);

        Assert.Equal(latitude, coordinate.Latitude);
        Assert.Equal(longitude, coordinate.Longitude);
    }

    [Theory]
    [InlineData(90.0001d, 0d)]
    [InlineData(-90.0001d, 0d)]
    public void Rejects_latitude_outside_the_range(double latitude, double longitude)
    {
        var ex = Assert.Throws<DomainException>(() => GeoCoordinate.Create(latitude, longitude));

        Assert.Equal("geo.latitude.invalid", ex.Code);
    }

    [Theory]
    [InlineData(0d, 180.0001d)]
    [InlineData(0d, -180.0001d)]
    public void Rejects_longitude_outside_the_range(double latitude, double longitude)
    {
        var ex = Assert.Throws<DomainException>(() => GeoCoordinate.Create(latitude, longitude));

        Assert.Equal("geo.longitude.invalid", ex.Code);
    }

    [Theory]
    [InlineData(double.NaN, 0d, "geo.latitude.invalid")]
    [InlineData(double.PositiveInfinity, 0d, "geo.latitude.invalid")]
    [InlineData(double.NegativeInfinity, 0d, "geo.latitude.invalid")]
    [InlineData(0d, double.NaN, "geo.longitude.invalid")]
    [InlineData(0d, double.PositiveInfinity, "geo.longitude.invalid")]
    public void Rejects_non_finite_values(double latitude, double longitude, string code)
    {
        var ex = Assert.Throws<DomainException>(() => GeoCoordinate.Create(latitude, longitude));

        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public void Equality_is_by_value()
    {
        Assert.Equal(GeoCoordinate.Create(4.65d, -74.06d), GeoCoordinate.Create(4.65d, -74.06d));
        Assert.NotEqual(GeoCoordinate.Create(4.65d, -74.06d), GeoCoordinate.Create(-74.06d, 4.65d));
    }
}

public sealed class ServiceRadiusTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(3000)]
    [InlineData(50_000)]
    public void Accepts_radius_inside_the_bounds(int meters)
    {
        Assert.Equal(meters, ServiceRadius.Create(meters).Meters);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(50_001)]
    public void Rejects_radius_outside_the_bounds(int meters)
    {
        var ex = Assert.Throws<DomainException>(() => ServiceRadius.Create(meters));

        Assert.Equal("service_area.radius.invalid", ex.Code);
    }
}

public sealed class StoreServiceAreaTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);

    [Fact]
    public void First_configuration_is_disabled_until_somebody_opens_the_store()
    {
        var area = Create();

        Assert.False(area.IsEnabled);
        Assert.Equal(4.65d, area.Latitude);
        Assert.Equal(-74.06d, area.Longitude);
        Assert.Equal(3000, area.ServiceRadiusMeters);
        Assert.Equal(Now, area.CreatedAt);
        Assert.Equal(Now, area.UpdatedAt);
    }

    [Fact]
    public void Create_requires_tenant_and_store()
    {
        var center = GeoCoordinate.Create(4.65d, -74.06d);
        var radius = ServiceRadius.Create(3000);

        Assert.Equal(
            "service_area.tenant.required",
            Assert.Throws<DomainException>(() =>
                StoreServiceArea.Create(Guid.Empty, Guid.CreateVersion7(), center, radius, Now)).Code);

        Assert.Equal(
            "service_area.store.required",
            Assert.Throws<DomainException>(() =>
                StoreServiceArea.Create(Guid.CreateVersion7(), Guid.Empty, center, radius, Now)).Code);
    }

    [Fact]
    public void Reconfiguring_with_the_same_values_is_a_no_op()
    {
        var area = Create();

        var changed = area.Configure(GeoCoordinate.Create(4.65d, -74.06d), ServiceRadius.Create(3000), Later);

        Assert.False(changed);
        Assert.Equal(Now, area.UpdatedAt);
    }

    [Theory]
    [InlineData(4.70d, -74.06d, 3000)]
    [InlineData(4.65d, -74.10d, 3000)]
    [InlineData(4.65d, -74.06d, 4000)]
    public void Reconfiguring_with_a_different_value_moves_the_area(double latitude, double longitude, int meters)
    {
        var area = Create();

        var changed = area.Configure(GeoCoordinate.Create(latitude, longitude), ServiceRadius.Create(meters), Later);

        Assert.True(changed);
        Assert.Equal(latitude, area.Latitude);
        Assert.Equal(longitude, area.Longitude);
        Assert.Equal(meters, area.ServiceRadiusMeters);
        Assert.Equal(Later, area.UpdatedAt);
    }

    [Fact]
    public void Enable_is_idempotent()
    {
        var area = Create();

        Assert.True(area.Enable(Later));
        Assert.True(area.IsEnabled);
        Assert.Equal(Later, area.UpdatedAt);

        Assert.False(area.Enable(Later.AddHours(1)));
        Assert.Equal(Later, area.UpdatedAt);
    }

    [Fact]
    public void Disable_is_idempotent()
    {
        var area = Create();
        area.Enable(Later);

        Assert.True(area.Disable(Later.AddHours(1)));
        Assert.False(area.IsEnabled);
        Assert.Equal(Later.AddHours(1), area.UpdatedAt);

        Assert.False(area.Disable(Later.AddHours(2)));
        Assert.Equal(Later.AddHours(1), area.UpdatedAt);
    }

    [Fact]
    public void Disable_on_a_freshly_created_area_changes_nothing()
    {
        var area = Create();

        Assert.False(area.Disable(Later));
        Assert.Equal(Now, area.UpdatedAt);
    }

    [Fact]
    public void Belongs_to_its_own_tenant_only()
    {
        var tenantId = Guid.CreateVersion7();
        var area = StoreServiceArea.Create(
            tenantId,
            Guid.CreateVersion7(),
            GeoCoordinate.Create(4.65d, -74.06d),
            ServiceRadius.Create(3000),
            Now);

        Assert.True(area.BelongsTo(tenantId));
        Assert.False(area.BelongsTo(Guid.CreateVersion7()));
    }

    [Fact]
    public void Not_configured_is_reported_with_a_stable_code()
    {
        Assert.Equal("service_area.not_configured", StoreServiceArea.NotConfigured().Code);
    }

    private static StoreServiceArea Create() => StoreServiceArea.Create(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        GeoCoordinate.Create(4.65d, -74.06d),
        ServiceRadius.Create(3000),
        Now);
}
