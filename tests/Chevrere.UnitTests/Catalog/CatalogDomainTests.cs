using Chevrere.Modules.Catalog.Domain;
using Chevrere.Modules.Catalog.Domain.ValueObjects;
using Chevrere.SharedKernel.Domain;

namespace Chevrere.UnitTests.Catalog;

public sealed class CatalogDomainTests
{
    [Fact]
    public void Category_create_activate_and_deactivate()
    {
        var category = Category.Create(
            CategoryCode.Create("BEBIDAS"),
            "Bebidas",
            Slug.FromName("Bebidas"),
            "Refrigerios",
            1,
            FixedClock.Now);

        Assert.True(category.IsActive);
        category.Deactivate(FixedClock.Now);
        Assert.Equal(CategoryStatus.Inactive, category.Status);
        category.Activate(FixedClock.Now);
        Assert.True(category.IsActive);
    }

    [Fact]
    public void Product_create_activate_and_deactivate()
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

        Assert.True(product.IsActive);
        product.Deactivate(FixedClock.Now);
        Assert.Equal(GlobalProductStatus.Inactive, product.Status);
        product.Activate(FixedClock.Now);
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
    public void Store_product_enable_disable_and_availability()
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
        Assert.True(CommercialAvailability.IsAvailable(category, product, offering));

        offering.Disable(FixedClock.Now);
        Assert.False(offering.IsEnabled);
        Assert.False(CommercialAvailability.IsAvailable(category, product, offering));

        offering.Enable(product, FixedClock.Now);
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
