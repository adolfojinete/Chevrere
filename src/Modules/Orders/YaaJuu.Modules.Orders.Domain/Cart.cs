using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.Modules.Orders.Domain;

/// <summary>
/// One consumer's active shopping cart, bound to the dark store that currently covers them.
/// Converted carts are immutable: the order snapshot owns the purchased lines after that.
/// </summary>
public sealed class Cart : AggregateRoot
{
    private readonly List<CartItem> _items = [];

    private Cart()
    {
    }

    public Guid ConsumerUserId { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public CartStatus Status { get; private set; }

    public DateTimeOffset? ConvertedAt { get; private set; }

    public IReadOnlyList<CartItem> Items => _items;

    public static Cart Start(Guid consumerUserId, Guid tenantId, Guid storeId, DateTimeOffset utcNow)
    {
        if (consumerUserId == Guid.Empty || tenantId == Guid.Empty || storeId == Guid.Empty)
        {
            throw new DomainException(
                "cart.identity.required",
                "A cart requires a consumer, a tenant and a store.");
        }

        return new Cart
        {
            Id = Guid.CreateVersion7(),
            ConsumerUserId = consumerUserId,
            TenantId = tenantId,
            StoreId = storeId,
            Status = CartStatus.Active,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    /// <summary>
    /// Moves an empty cart to a new fulfillment store. A cart with items cannot relocate: the
    /// application must reject that as <c>cart.fulfillment_changed</c> before calling this.
    /// </summary>
    public void RelocateEmpty(Guid tenantId, Guid storeId, DateTimeOffset utcNow)
    {
        EnsureActive();
        if (_items.Count > 0)
        {
            throw new DomainException(
                "cart.fulfillment_changed",
                "A cart with items cannot change fulfillment store.");
        }

        if (tenantId == Guid.Empty || storeId == Guid.Empty)
        {
            throw new DomainException(
                "cart.identity.required",
                "A cart requires a tenant and a store.");
        }

        TenantId = tenantId;
        StoreId = storeId;
        UpdatedAt = utcNow;
    }

    /// <returns>true when the line was added or its quantity changed.</returns>
    public bool SetItem(Guid productId, long quantity, DateTimeOffset utcNow)
    {
        EnsureActive();
        if (quantity <= 0)
        {
            throw new DomainException("cart.item.quantity.invalid", "Quantity must be greater than zero.");
        }

        var existing = _items.Find(i => i.GlobalProductId == productId);
        if (existing is null)
        {
            _items.Add(CartItem.Create(Id, TenantId, StoreId, productId, quantity, utcNow));
            UpdatedAt = utcNow;
            return true;
        }

        if (existing.Quantity == quantity)
        {
            return false;
        }

        existing.SetQuantity(quantity, utcNow);
        UpdatedAt = utcNow;
        return true;
    }

    /// <returns>true when a line was actually removed.</returns>
    public bool RemoveItem(Guid productId, DateTimeOffset utcNow)
    {
        EnsureActive();
        var existing = _items.Find(i => i.GlobalProductId == productId);
        if (existing is null)
        {
            return false;
        }

        _items.Remove(existing);
        UpdatedAt = utcNow;
        return true;
    }

    /// <returns>true when the cart had lines to clear.</returns>
    public bool Clear(DateTimeOffset utcNow)
    {
        EnsureActive();
        if (_items.Count == 0)
        {
            return false;
        }

        _items.Clear();
        UpdatedAt = utcNow;
        return true;
    }

    public void Convert(DateTimeOffset utcNow)
    {
        if (Status == CartStatus.Converted)
        {
            throw new DomainException("cart.already_converted", "This cart has already been converted into an order.");
        }

        EnsureActive();
        if (_items.Count == 0)
        {
            throw new DomainException("cart.empty", "An empty cart cannot be converted into an order.");
        }

        Status = CartStatus.Converted;
        ConvertedAt = utcNow;
        UpdatedAt = utcNow;
    }

    private void EnsureActive()
    {
        if (Status != CartStatus.Active)
        {
            throw new DomainException("cart.already_converted", "A converted cart cannot be mutated.");
        }
    }
}
