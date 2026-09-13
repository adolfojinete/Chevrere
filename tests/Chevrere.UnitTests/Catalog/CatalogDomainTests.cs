using Chevrere.Modules.Catalog.Domain;
using Chevrere.Modules.Catalog.Domain.ValueObjects;
using Chevrere.SharedKernel.Domain;

namespace Chevrere.UnitTests.Catalog;

public sealed class CatalogDomainTests
{
    [Fact]
    public void Category_activate_and_deactivate_are_idempotent()
    {
        var category = Category.Create(
            CategoryCode.Create("BEBIDAS"),
            "Bebidas",
            Slug.FromName("Bebidas"),
            "Refrigerios",
            1,
            FixedClock.Now);

        Assert.True(category.IsActive);
        Assert.False(category.Activate(FixedClock.Now.AddMinutes(1)));
        Assert.Equal(FixedClock.Now, category.UpdatedAt);

        Assert.True(category.Deactivate(FixedClock.Now.AddMinutes(2)));
        Assert.Equal(CategoryStatus.Inactive, category.Status);
        Assert.Equal(FixedClock.Now.AddMinutes(2), category.UpdatedAt);

        Assert.False(category.Deactivate(FixedClock.Now.AddMinutes(3)));
        Assert.Equal(FixedClock.Now.AddMinutes(2), category.UpdatedAt);

        Assert.True(category.Activate(FixedClock.Now.AddMinutes(4)));
        Assert.True(category.IsActive);
    }

    [Fact]
    public void Product_activate_and_deactivate_are_idempotent()
    {
        var category = Category.Create(CategoryCode.Create("BEB"), "Bebidas", Slug.FromName("bebidas"), null, 0, FixedClock.Now);
        var product = GlobalProduct.Create(
            category,
            Sku.Create("COCACOLA-15"),
            "Coca-Cola Original",
            Slug.FromName("Coca-Cola Original 1.5L"),
            "Coca-Cola",
            "1.5 L",
            null,
            Barcode.Create("7701234567890"),
            FixedClock.Now);

        Assert.False(product.Activate(FixedClock.Now.AddMinutes(1)));
        Assert.Equal(FixedClock.Now, product.UpdatedAt);

        Assert.True(product.Deactivate(FixedClock.Now.AddMinutes(2)));
        Assert.Equal(GlobalProductStatus.Inactive, product.Status);
        Assert.False(product.Deactivate(FixedClock.Now.AddMinutes(3)));
        Assert.Equal(FixedClock.Now.AddMinutes(2), product.UpdatedAt);

        Assert.True(product.Activate(FixedClock.Now.AddMinutes(4)));
        Assert.True(product.IsActive);
    }

    [Fact]
    public void Store_cannot_enable_inactive_global_product()
    {
        var category = Category.Create(CategoryCode.Create("BEB"), "Bebidas", Slug.FromName("bebidas"), null, 0, FixedClock.Now);
        var product = GlobalProduct.Create(
            category,
            Sku.Create("COCACOLA-15"),
            "Coca-Cola Original",
            Slug.FromName("coca-cola-original"),
            "Coca-Cola",
            "1.5 L",
            null,
            null,
            FixedClock.Now);
        product.Deactivate(FixedClock.Now);

        var ex = Assert.Throws<DomainException>(() =>
            StoreProduct.EnableForStore(Guid.CreateVersion7(), Guid.CreateVersion7(), product, FixedClock.Now));
        Assert.Equal("product.not_active", ex.Code);
    }

    [Fact]
    public void Store_product_enable_disable_are_idempotent()
    {
        var category = Category.Create(CategoryCode.Create("BEB"), "Bebidas", Slug.FromName("bebidas"), null, 0, FixedClock.Now);
        var product = GlobalProduct.Create(
            category,
            Sku.Create("COCACOLA-15"),
            "Coca-Cola Original",
            Slug.FromName("coca-cola-original"),
            "Coca-Cola",
            "1.5 L",
            null,
            null,
            FixedClock.Now);
        var tenantId = Guid.CreateVersion7();
        var storeId = Guid.CreateVersion7();
        var offering = StoreProduct.EnableForStore(tenantId, storeId, product, FixedClock.Now);

        Assert.True(offering.IsEnabled);
        Assert.False(offering.Enable(product, FixedClock.Now.AddMinutes(1)));
        Assert.Equal(FixedClock.Now, offering.UpdatedAt);

        Assert.True(offering.Disable(FixedClock.Now.AddMinutes(2)));
        Assert.False(offering.IsEnabled);
        Assert.False(offering.Disable(FixedClock.Now.AddMinutes(3)));
        Assert.Equal(FixedClock.Now.AddMinutes(2), offering.UpdatedAt);

        Assert.True(offering.Enable(product, FixedClock.Now.AddMinutes(4)));
        Assert.True(CommercialAvailability.IsAvailable(category, product, offering));

        category.Deactivate(FixedClock.Now);
        Assert.False(CommercialAvailability.IsAvailable(category, product, offering));
        Assert.False(CommercialAvailability.IsBrowsable(category, product));
    }

    [Fact]
    public void Slug_and_sku_normalize()
    {
        Assert.Equal("coca-cola-original-1-5l", Slug.FromName("Coca-Cola Original 1.5L").Value);
        Assert.Equal("COCACOLA-15", Sku.Create("cocacola-15").Value);
        Assert.Null(Barcode.Create(null));
        Assert.Throws<DomainException>(() => Barcode.Create("ABC"));
    }
}
