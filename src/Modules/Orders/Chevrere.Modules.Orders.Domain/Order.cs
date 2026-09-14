using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Domain.ValueObjects;

namespace Chevrere.Modules.Orders.Domain;

/// <summary>
/// Consumer order placed from a cart. Inventory is reserved while the order is PendingPayment;
/// Confirm does not commit stock — that happens on a later payment/fulfillment phase.
/// </summary>
public sealed class Order : AggregateRoot
{
    private readonly List<OrderItem> _items = [];

    private Order()
    {
        Number = null!;
        Currency = null!;
    }

    public string Number { get; private set; }

    public Guid ConsumerUserId { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid SourceCartId { get; private set; }

    public OrderStatus Status { get; private set; }

    public decimal SubtotalAmount { get; private set; }

    public decimal TotalAmount { get; private set; }

    public string Currency { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancelReason { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;

    public static Order Place(
        string number,
        Guid consumerUserId,
        Guid tenantId,
        Guid storeId,
        Guid sourceCartId,
        IReadOnlyList<OrderLineSnapshot> lines,
        TimeSpan reservationTtl,
        DateTimeOffset utcNow)
    {
        if (consumerUserId == Guid.Empty || tenantId == Guid.Empty || storeId == Guid.Empty
            || sourceCartId == Guid.Empty)
        {
            throw new DomainException(
                "order.identity.required",
                "An order requires a consumer, a tenant, a store and a source cart.");
        }

        if (reservationTtl <= TimeSpan.Zero)
        {
            throw new DomainException("order.ttl.invalid", "Reservation TTL must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new DomainException("order.items.required", "An order requires at least one line.");
        }

        if (lines.DistinctBy(l => l.GlobalProductId).Count() != lines.Count)
        {
            throw new DomainException(
                "order.item.duplicate",
                "An order cannot contain the same product twice.");
        }

        var currency = lines[0].UnitPrice.Currency;
        if (lines.Any(l => !string.Equals(l.UnitPrice.Currency, currency, StringComparison.Ordinal)))
        {
            throw new DomainException(
                "order.currency.mixed",
                "All lines of an order must share the same currency.");
        }

        if (!string.Equals(currency, CurrencyCodes.Cop, StringComparison.Ordinal))
        {
            throw new DomainException("order.currency.unsupported", "Orders only accept COP.");
        }

        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            Number = Guard.NotNullOrWhiteSpace(number, nameof(number), 40),
            ConsumerUserId = consumerUserId,
            TenantId = tenantId,
            StoreId = storeId,
            SourceCartId = sourceCartId,
            Status = OrderStatus.PendingPayment,
            Currency = currency,
            ExpiresAt = utcNow.Add(reservationTtl),
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        foreach (var line in lines)
        {
            order._items.Add(OrderItem.Create(order.Id, tenantId, storeId, line, utcNow));
        }

        var total = order._items.Sum(i => i.LineTotalAmount);
        order.SubtotalAmount = total;
        order.TotalAmount = total;
        return order;
    }

    /// <returns>true when the order actually moved to Cancelled.</returns>
    public bool Cancel(string? reason, DateTimeOffset utcNow)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return false;
        }

        if (Status is OrderStatus.Confirmed or OrderStatus.Expired)
        {
            throw new DomainException(
                "order.cannot_cancel",
                $"An order in status {Status} cannot be cancelled.");
        }

        Status = OrderStatus.Cancelled;
        CancelReason = NormalizeReason(reason);
        CancelledAt = utcNow;
        UpdatedAt = utcNow;
        return true;
    }

    /// <returns>true when the order actually moved to Expired.</returns>
    public bool Expire(DateTimeOffset utcNow)
    {
        if (Status == OrderStatus.Expired)
        {
            return false;
        }

        if (Status != OrderStatus.PendingPayment)
        {
            throw new DomainException(
                "order.cannot_expire",
                $"An order in status {Status} cannot expire.");
        }

        Status = OrderStatus.Expired;
        UpdatedAt = utcNow;
        return true;
    }

    /// <returns>true when the order actually moved to Confirmed. Does not commit inventory.</returns>
    public bool Confirm(DateTimeOffset utcNow)
    {
        if (Status == OrderStatus.Confirmed)
        {
            return false;
        }

        if (Status != OrderStatus.PendingPayment)
        {
            throw new DomainException(
                "order.cannot_confirm",
                $"An order in status {Status} cannot be confirmed.");
        }

        Status = OrderStatus.Confirmed;
        UpdatedAt = utcNow;
        return true;
    }

    public bool BelongsToConsumer(Guid consumerUserId) => ConsumerUserId == consumerUserId;

    public bool BelongsTo(Guid tenantId, Guid storeId) => TenantId == tenantId && StoreId == storeId;

    private static string? NormalizeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : Guard.NotNullOrWhiteSpace(reason, nameof(reason), 1000);
}
