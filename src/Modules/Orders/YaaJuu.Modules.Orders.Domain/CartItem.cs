using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.Modules.Orders.Domain;

public sealed class CartItem : Entity
{
    private CartItem()
    {
    }

    public Guid CartId { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid GlobalProductId { get; private set; }

    public long Quantity { get; private set; }

    internal static CartItem Create(
        Guid cartId,
        Guid tenantId,
        Guid storeId,
        Guid globalProductId,
        long quantity,
        DateTimeOffset utcNow)
    {
        if (globalProductId == Guid.Empty)
        {
            throw new DomainException("cart.item.product.required", "Every cart line requires a product.");
        }

        if (quantity <= 0)
        {
            throw new DomainException("cart.item.quantity.invalid", "Quantity must be greater than zero.");
        }

        return new CartItem
        {
            Id = Guid.CreateVersion7(),
            CartId = cartId,
            TenantId = tenantId,
            StoreId = storeId,
            GlobalProductId = globalProductId,
            Quantity = quantity,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    internal void SetQuantity(long quantity, DateTimeOffset utcNow)
    {
        if (quantity <= 0)
        {
            throw new DomainException("cart.item.quantity.invalid", "Quantity must be greater than zero.");
        }

        Quantity = quantity;
        UpdatedAt = utcNow;
    }
}
