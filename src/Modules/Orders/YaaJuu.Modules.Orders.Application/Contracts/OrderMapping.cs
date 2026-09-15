using YaaJuu.Modules.Orders.Application.Contracts;
using YaaJuu.Modules.Orders.Domain;

namespace YaaJuu.Modules.Orders.Application.Contracts;

internal static class OrderMapping
{
    public static ConsumerOrderDto ToConsumerDto(Order order) =>
        new(
            order.Id,
            order.Number,
            order.Status,
            order.SubtotalAmount,
            order.TotalAmount,
            order.Currency,
            order.CreatedAt,
            order.ExpiresAt,
            order.CancelledAt,
            order.CancelReason,
            [.. order.Items.Select(ToConsumerItem)]);

    public static ConsumerOrderSummaryDto ToConsumerSummary(Order order) =>
        new(order.Id, order.Number, order.Status, order.TotalAmount, order.Currency, order.CreatedAt, order.ExpiresAt);

    public static BusinessOrderDto ToBusinessDto(Order order) =>
        new(
            order.Id,
            order.Number,
            order.ConsumerUserId,
            order.SourceCartId,
            order.Status,
            order.SubtotalAmount,
            order.TotalAmount,
            order.Currency,
            order.CreatedAt,
            order.ExpiresAt,
            order.CancelledAt,
            order.CancelReason,
            [.. order.Items.Select(ToBusinessItem)]);

    public static BusinessOrderSummaryDto ToBusinessSummary(Order order) =>
        new(
            order.Id,
            order.Number,
            order.ConsumerUserId,
            order.Status,
            order.TotalAmount,
            order.Currency,
            order.CreatedAt,
            order.ExpiresAt);

    public static AdminOrderDto ToAdminDto(Order order) =>
        new(
            order.Id,
            order.Number,
            order.ConsumerUserId,
            order.TenantId,
            order.StoreId,
            order.SourceCartId,
            order.Status,
            order.SubtotalAmount,
            order.TotalAmount,
            order.Currency,
            order.CreatedAt,
            order.ExpiresAt,
            order.CancelledAt,
            order.CancelReason,
            [.. order.Items.Select(ToBusinessItem)]);

    private static ConsumerOrderItemDto ToConsumerItem(OrderItem item) =>
        new(
            item.GlobalProductId,
            item.Sku,
            item.Name,
            item.Brand,
            item.Presentation,
            item.Quantity,
            item.UnitPriceAmount,
            item.UnitPriceCurrency,
            item.LineTotalAmount);

    private static BusinessOrderItemDto ToBusinessItem(OrderItem item) =>
        new(
            item.Id,
            item.GlobalProductId,
            item.Sku,
            item.Name,
            item.Brand,
            item.Presentation,
            item.Quantity,
            item.UnitPriceAmount,
            item.UnitPriceCurrency,
            item.LineTotalAmount);
}
