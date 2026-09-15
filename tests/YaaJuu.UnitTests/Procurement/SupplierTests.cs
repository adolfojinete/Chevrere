using YaaJuu.Modules.Procurement.Domain;
using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.UnitTests.Procurement;

public sealed class SupplierTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();

    [Fact]
    public void Create_normalizes_code_and_email_and_starts_active()
    {
        var supplier = Supplier.Create(
            TenantId,
            "  prov-01 ",
            "  Distribuidora Andina  ",
            new SupplierContactDetails(
                TaxIdentification: " 900123456-1 ",
                ContactName: " Ana Ruiz ",
                Email: "  Ventas@Andina.CO ",
                Phone: " +573001112233 ",
                Address: " Calle 80 # 20-10 ",
                Notes: " Entrega martes "),
            FixedClock.Now);

        Assert.Equal("PROV-01", supplier.Code);
        Assert.Equal("Distribuidora Andina", supplier.Name);
        Assert.Equal("ventas@andina.co", supplier.Email);
        Assert.Equal("900123456-1", supplier.TaxIdentification);
        Assert.Equal("Ana Ruiz", supplier.ContactName);
        Assert.True(supplier.IsActive);
        Assert.Equal(TenantId, supplier.TenantId);
    }

    [Fact]
    public void Create_requires_tenant_code_name_and_valid_email()
    {
        Assert.Throws<DomainException>(() => Supplier.Create(
            Guid.Empty, "PROV-01", "Andina", new SupplierContactDetails(), FixedClock.Now));
        Assert.Throws<DomainException>(() => Supplier.Create(
            TenantId, "  ", "Andina", new SupplierContactDetails(), FixedClock.Now));
        Assert.Throws<DomainException>(() => Supplier.Create(
            TenantId, "PROV-01", " ", new SupplierContactDetails(), FixedClock.Now));

        var invalidEmail = Assert.Throws<DomainException>(() => Supplier.Create(
            TenantId, "PROV-01", "Andina", new SupplierContactDetails(Email: "no-arroba"), FixedClock.Now));
        Assert.Equal("supplier.email.invalid", invalidEmail.Code);
    }

    [Fact]
    public void Blank_optional_fields_become_null()
    {
        var supplier = Supplier.Create(
            TenantId,
            "PROV-02",
            "Andina",
            new SupplierContactDetails(TaxIdentification: "   ", ContactName: "", Notes: " "),
            FixedClock.Now);

        Assert.Null(supplier.TaxIdentification);
        Assert.Null(supplier.ContactName);
        Assert.Null(supplier.Notes);
        Assert.Null(supplier.Email);
    }

    [Fact]
    public void Update_replaces_contact_details_without_touching_code()
    {
        var supplier = Supplier.Create(
            TenantId, "PROV-03", "Andina", new SupplierContactDetails(Phone: "+573001112233"), FixedClock.Now);
        var later = FixedClock.Now.AddHours(1);

        supplier.Update("Andina SAS", new SupplierContactDetails(ContactName: "Luis"), later);

        Assert.Equal("PROV-03", supplier.Code);
        Assert.Equal("Andina SAS", supplier.Name);
        Assert.Equal("Luis", supplier.ContactName);
        Assert.Null(supplier.Phone);
        Assert.Equal(later, supplier.UpdatedAt);
    }

    [Fact]
    public void Activate_and_deactivate_report_whether_state_changed()
    {
        var supplier = Supplier.Create(
            TenantId, "PROV-04", "Andina", new SupplierContactDetails(), FixedClock.Now);

        Assert.False(supplier.Activate(FixedClock.Now.AddMinutes(1)));
        Assert.True(supplier.Deactivate(FixedClock.Now.AddMinutes(2)));
        Assert.False(supplier.Deactivate(FixedClock.Now.AddMinutes(3)));
        Assert.True(supplier.Activate(FixedClock.Now.AddMinutes(4)));
        Assert.True(supplier.IsActive);
    }

    [Fact]
    public void Inactive_supplier_cannot_receive_orders()
    {
        var supplier = Supplier.Create(
            TenantId, "PROV-05", "Andina", new SupplierContactDetails(), FixedClock.Now);
        supplier.EnsureCanReceiveOrders();

        supplier.Deactivate(FixedClock.Now.AddMinutes(1));
        var ex = Assert.Throws<DomainException>(supplier.EnsureCanReceiveOrders);
        Assert.Equal("supplier.not_active", ex.Code);
    }
}
