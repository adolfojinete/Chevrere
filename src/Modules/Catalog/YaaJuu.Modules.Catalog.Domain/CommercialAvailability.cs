namespace YaaJuu.Modules.Catalog.Domain;

public static class CommercialAvailability
{
    public static bool IsAvailable(Category category, GlobalProduct product, StoreProduct storeProduct)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(storeProduct);

        return category.IsActive && product.IsActive && storeProduct.IsEnabled;
    }

    public static bool IsBrowsable(Category category, GlobalProduct product)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(product);

        return category.IsActive && product.IsActive;
    }
}
