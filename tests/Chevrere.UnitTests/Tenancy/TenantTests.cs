using Chevrere.Modules.Tenancy.Domain;
using Chevrere.Modules.Tenancy.Domain.ValueObjects;
using Chevrere.SharedKernel.Domain;

namespace Chevrere.UnitTests.Tenancy;

public sealed class TenantTests
{
    [Fact]
    public void Create_valid_tenant_starts_pending()
    {
        var tenant = Tenant.Create(TenantCode.Create("op-norte"), "Operador Norte", FixedClock.Now);

        Assert.NotEqual(Guid.Empty, tenant.Id);
        Assert.Equal("OP-NORTE", tenant.Code);
        Assert.Equal(TenantStatus.Pending, tenant.Status);
    }

    [Fact]
    public void Tenant_code_must_be_unique_shape()
    {
        Assert.Throws<DomainException>(() => TenantCode.Create("ab"));
        Assert.Throws<DomainException>(() => TenantCode.Create("invalid code"));
        Assert.Equal("BOGOTA-01", TenantCode.Create("bogota-01").Value);
    }
}
