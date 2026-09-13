using Chevrere.Modules.Tenancy.Domain;
using Chevrere.SharedKernel.Domain;

namespace Chevrere.UnitTests.SharedKernel;

public sealed class GuardAndEntityTests
{
    [Fact]
    public void Guard_rejects_blank_and_too_long()
    {
        Assert.Throws<DomainException>(() => Guard.NotNullOrWhiteSpace(" ", "name"));
        Assert.Throws<DomainException>(() => Guard.NotNullOrWhiteSpace(new string('a', 300), "name"));
        Assert.Equal("ok", Guard.NotNullOrWhiteSpace(" ok ", "name"));
    }

    [Fact]
    public void Guard_rejects_null_reference()
    {
        Assert.Throws<DomainException>(() => Guard.NotNull<string>(null, "name"));
        Assert.Equal("x", Guard.NotNull("x", "name"));
    }

    [Fact]
    public void Entities_with_same_id_are_equal()
    {
        var tenant = TestEntities.Tenant();
        var same = tenant;
        Assert.True(tenant.Equals((object)same));
        Assert.False(tenant.Equals(null));
        Assert.NotEqual(0, tenant.GetHashCode());
    }

    [Fact]
    public void Different_tenants_are_not_equal()
    {
        var a = TestEntities.Tenant("AAA-1");
        var b = TestEntities.Tenant("BBB-1");
        Assert.False(a.Equals(b));
    }
}
