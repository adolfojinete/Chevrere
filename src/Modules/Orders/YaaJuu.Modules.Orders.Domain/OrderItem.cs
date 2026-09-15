using YaaJuu.SharedKernel.Domain;
using YaaJuu.SharedKernel.Domain.ValueObjects;

namespace YaaJuu.Modules.Orders.Domain;

/// <summary>
/// Immutable commercial snapshot of one purchased product. The id is assigned in the domain before
/// persist so inventory can reference it as <c>OrderItem</c> on the same transaction.
/// </summary>
public sealed class OrderItem : Entity
{
    private OrderItem()
    {
        Sku = null!;
        Name = null!;
        Brand = null!;
        Presentation = null!;
        UnitPriceCurrency = null!;
    }

    public Guid OrderId { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid GlobalProductId { get; private set; }

    public string Sku { get; private set; }

    public string Name { get; private set; }

    public string Brand { get; private set; }

    public string Presentation { get; private set; }

    public long Quantity { get; private set; }

    public decimal UnitPriceAmount { get; private set; }

    public string UnitPriceCurrency { get; private set; }

    public decimal LineTotalAmount { get; private set; }

    public Money UnitPrice() => Money.FromPersistence(UnitPriceAmount, UnitPriceCurrency);

    internal static OrderItem Create(
        Guid orderId,
        Guid tenantId,
        Guid storeId,
        OrderLineSnapshot snapshot,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.GlobalProductId == Guid.Empty)
        {
            throw new DomainException("order.item.product.required", "Every order line requires a product.");
        }

        if (snapshot.Quantity <= 0)
        {
            throw new DomainException("order.item.quantity.invalid", "Quantity must be greater than zero.");
        }

        var lineTotal = checked(snapshot.UnitPrice.Amount * snapshot.Quantity);
        return new OrderItem
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            TenantId = tenantId,
            StoreId = storeId,
            GlobalProductId = snapshot.GlobalProductId,
            Sku = Guard.NotNullOrWhiteSpace(snapshot.Sku, nameof(snapshot.Sku), 64),
            Name = Guard.NotNullOrWhiteSpace(snapshot.Name, nameof(snapshot.Name), 160),
            Brand = Guard.NotNullOrWhiteSpace(snapshot.Brand, nameof(snapshot.Brand), 80),
            Presentation = Guard.NotNullOrWhiteSpace(snapshot.Presentation, nameof(snapshot.Presentation), 80),
            Quantity = snapshot.Quantity,
            UnitPriceAmount = snapshot.UnitPrice.Amount,
            UnitPriceCurrency = snapshot.UnitPrice.Currency,
            LineTotalAmount = lineTotal,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }
}

/// <summary>
/// Commercial facts frozen at checkout. Later catalog or price changes must not rewrite them.
/// </summary>
public sealed record OrderLineSnapshot(
    Guid GlobalProductId,
    string Sku,
    string Name,
    string Brand,
    string Presentation,
    long Quantity,
    Money UnitPrice);
