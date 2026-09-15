using YaaJuu.Modules.Orders.Domain;
using YaaJuu.SharedKernel.Domain.ValueObjects;

namespace YaaJuu.Modules.Orders.Application.Abstractions;

public interface IOrderStore
{
    Task<Cart?> GetActiveCartAsync(Guid consumerUserId, CancellationToken cancellationToken);

    Task<Cart?> GetCartAsync(Guid cartId, CancellationToken cancellationToken);

    void AddCart(Cart cart);

    uint GetCartVersion(Cart cart);

    /// <summary>
    /// EF Core treats new field-backed children with client GUIDs as Modified (UPDATE 0 rows)
    /// and can zero xmin when the collection changes. Call immediately before SaveChanges.
    /// </summary>
    void PrepareCartForSave(Cart cart);

    Task<Order?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken);

    Task<Order?> GetOrderForConsumerAsync(Guid orderId, Guid consumerUserId, CancellationToken cancellationToken);

    Task<Order?> GetOrderForStoreAsync(Guid tenantId, Guid storeId, Guid orderId, CancellationToken cancellationToken);

    Task<Order?> ReadOrderAsync(Guid orderId, CancellationToken cancellationToken);

    void AddOrder(Order order);

    Task<(IReadOnlyList<Order> Items, int Total)> ListConsumerOrdersAsync(
        Guid consumerUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<(IReadOnlyList<Order> Items, int Total)> ListStoreOrdersAsync(
        Guid tenantId,
        Guid storeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListExpiredPendingOrderIdsAsync(
        DateTimeOffset utcNow,
        int batchSize,
        CancellationToken cancellationToken);

    void DiscardCart(Cart cart);

    void DiscardOrder(Order order);
}

public interface IOrderStoreAccess
{
    Task<Guid?> GetStoreTenantIdAsync(Guid storeId, CancellationToken cancellationToken);
}

public interface IOrderNumberGenerator
{
    Task<string> NextOrderNumberAsync(CancellationToken cancellationToken);
}

public interface IOrderReservationPolicy
{
    TimeSpan ReservationTtl { get; }

    int ExpirationPollSeconds { get; }

    int ExpirationBatchSize { get; }

    bool ExpirationWorkerEnabled { get; }
}

/// <summary>
/// Commercial visibility + current price + available stock of a resolved store. Copied from the
/// Consumer catalog join so Orders never references Catalog, Pricing or Inventory modules.
/// </summary>
public interface IOrderCommercialReadStore
{
    Task<IReadOnlyList<OrderCommercialProduct>> GetProductsAsync(
        Guid tenantId,
        Guid storeId,
        IReadOnlyList<Guid> globalProductIds,
        CancellationToken cancellationToken);
}

public sealed record OrderCommercialProduct(
    Guid GlobalProductId,
    string Sku,
    string Name,
    string Brand,
    string Presentation,
    decimal Amount,
    string Currency,
    long Available)
{
    public Money UnitPrice() => Money.Create(Amount, Currency);
}
