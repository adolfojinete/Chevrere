using YaaJuu.Modules.Subscriptions.Domain.ValueObjects;
using YaaJuu.Modules.Tenancy.Domain;
using YaaJuu.Modules.Tenancy.Domain.ValueObjects;
using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.UnitTests.Tenancy;

public sealed class ValueObjectTests
{
    [Fact]
    public void Email_is_normalized_and_validated()
    {
        Assert.Equal("ops@example.com", EmailAddress.Create("  OPS@example.com ").Value);
        Assert.Throws<DomainException>(() => EmailAddress.Create("not-an-email"));
        Assert.True(EmailAddress.Create("a@b.com").Equals(EmailAddress.Create("A@B.COM")));
    }

    [Fact]
    public void Phone_requires_minimum_length()
    {
        Assert.Throws<DomainException>(() => PhoneNumber.Create("123"));
        Assert.Equal("+573001112233", PhoneNumber.Create(" +573001112233 ").Value);
    }

    [Fact]
    public void Identification_normalizes_number()
    {
        var id = Identification.Create(IdentificationType.Nit, "900.123.456");
        Assert.Equal("900123456", id.Number);
        Assert.Throws<DomainException>(() => Identification.Create(IdentificationType.Nit, "12"));
        Assert.Throws<DomainException>(() => Identification.Create((IdentificationType)99, "900123456"));
    }

    [Fact]
    public void Geo_requires_both_or_neither()
    {
        Assert.Null(GeoCoordinate.Create(null, null));
        Assert.Throws<DomainException>(() => GeoCoordinate.Create(1, null));
        Assert.Throws<DomainException>(() => GeoCoordinate.Create(100, 0));
        Assert.Throws<DomainException>(() => GeoCoordinate.Create(0, 200));
        var geo = GeoCoordinate.Create(4.71m, -74.07m);
        Assert.NotNull(geo);
        Assert.Equal(4.71m, geo.Latitude);
    }

    [Fact]
    public void Tenant_code_rejects_invalid_shapes()
    {
        Assert.Throws<DomainException>(() => TenantCode.Create(null));
        Assert.Throws<DomainException>(() => TenantCode.Create("AB"));
        Assert.Equal("OP-NORTE", TenantCode.Create("op-norte").ToString());
    }

    [Fact]
    public void Money_rejects_negative_and_bad_currency()
    {
        Assert.Throws<DomainException>(() => Money.Create(-1, "COP"));
        Assert.Throws<DomainException>(() => Money.Create(1, "PESO"));
        Assert.Equal(350000.00m, Money.Create(350000, "cop").Amount);
        Assert.Equal("COP", Money.Create(1, "cop").Currency);
    }
}
