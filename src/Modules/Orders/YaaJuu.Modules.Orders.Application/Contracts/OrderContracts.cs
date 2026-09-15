using YaaJuu.Modules.Orders.Domain;

namespace YaaJuu.Modules.Orders.Application.Contracts;

public sealed record OrderLocationRequest(double Latitude, double Longitude);

public sealed record ConsumerLocationQuantityRequest(double Latitude, double Longitude, long Quantity);

public sealed record CreateOrderRequest(double Latitude, double Longitude);

public sealed record CancelOrderRequest(string? Reason);

public sealed record CartViewDto(
    Guid? CartId,
    CartStatus? Status,
    bool ServiceAvailable,
    bool FulfillmentChanged,
    bool CanCreateOrder,
    IReadOnlyList<CartItemViewDto> Items);

public sealed record CartItemViewDto(
    Guid ProductId,
    long Quantity,
    decimal? CurrentUnitPrice,
    string? Currency,
    bool IsAvailable);

public sealed record ConsumerOrderSummaryDto(
    Guid Id,
    string Number,
    OrderStatus Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record ConsumerOrderDto(
    Guid Id,
    string Number,
    OrderStatus Status,
    decimal SubtotalAmount,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    IReadOnlyList<ConsumerOrderItemDto> Items);

public sealed record ConsumerOrderItemDto(
    Guid ProductId,
    string Sku,
    string Name,
    string Brand,
    string Presentation,
    long Quantity,
    decimal UnitPriceAmount,
    string Currency,
    decimal LineTotalAmount);

public sealed record BusinessOrderSummaryDto(
    Guid Id,
    string Number,
    Guid ConsumerUserId,
    OrderStatus Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record BusinessOrderDto(
    Guid Id,
    string Number,
    Guid ConsumerUserId,
    Guid SourceCartId,
    OrderStatus Status,
    decimal SubtotalAmount,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    IReadOnlyList<BusinessOrderItemDto> Items);

public sealed record BusinessOrderItemDto(
    Guid Id,
    Guid ProductId,
    string Sku,
    string Name,
    string Brand,
    string Presentation,
    long Quantity,
    decimal UnitPriceAmount,
    string Currency,
    decimal LineTotalAmount);

public sealed record AdminOrderDto(
    Guid Id,
    string Number,
    Guid ConsumerUserId,
    Guid TenantId,
    Guid StoreId,
    Guid SourceCartId,
    OrderStatus Status,
    decimal SubtotalAmount,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    IReadOnlyList<BusinessOrderItemDto> Items);
