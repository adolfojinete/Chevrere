using YaaJuu.Modules.Orders.Application.Abstractions;
using YaaJuu.Modules.Orders.Application.Commands;
using YaaJuu.Modules.Orders.Application.Contracts;
using YaaJuu.Modules.Orders.Domain;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Idempotency;
using YaaJuu.SharedKernel.Inventory;
using YaaJuu.SharedKernel.Results;

namespace YaaJuu.Modules.Orders.Application.Idempotency;

internal sealed record CreateOrderAttempt(
    Order Order,
    Cart Cart,
    IReadOnlyList<InventoryReservationLineResult> Reservations,
    IdempotentOperation Idempotency);

internal static class OrderFingerprints
{
    public static string ForCreate(
        Guid consumerUserId,
        Guid cartId,
        uint cartVersion,
        Guid tenantId,
        Guid storeId,
        IReadOnlyList<CartItem> items)
    {
        var parts = new List<string?>
        {
            IdempotencyOperations.OrdersCreate,
            consumerUserId.ToString("D"),
            cartId.ToString("D"),
            cartVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            tenantId.ToString("D"),
            storeId.ToString("D")
        };

        parts.AddRange(items
            .OrderBy(i => i.GlobalProductId)
            .Select(i => string.Join(
                ':',
                i.GlobalProductId.ToString("D"),
                IdempotencyFingerprint.Format(i.Quantity))));

        return IdempotencyFingerprint.Sha256([.. parts]);
    }
}

internal static class OrderIdempotencyReplay
{
    public static void DiscardAttempt(
        IOrderStore store,
        IIdempotencyStore idempotency,
        IAuditRecorder audit,
        IInventoryReservationService reservations,
        CreateOrderAttempt attempt)
    {
        reservations.DiscardPending(attempt.Reservations);
        store.DiscardOrder(attempt.Order);
        store.DiscardCart(attempt.Cart);
        idempotency.DiscardPending(attempt.Idempotency);
        audit.DiscardPending(AuditActions.OrderCreated, nameof(Order), attempt.Order.Id);
    }

    public static async Task<Result<ConsumerOrderDto>?> TryReplayAsync(
        IOrderStore store,
        IIdempotencyStore idempotency,
        IAuditRecorder audit,
        IInventoryReservationService reservations,
        CreateOrderAttempt attempt,
        Guid tenantId,
        Guid consumerUserId,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        DiscardAttempt(store, idempotency, audit, reservations, attempt);

        var winner = await idempotency.FindAsync(
            tenantId, IdempotencyOperations.OrdersCreate, idempotencyKey, cancellationToken);
        if (winner is null)
        {
            return null;
        }

        if (!string.Equals(winner.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return Result.Failure<ConsumerOrderDto>(
                Error.Conflict("idempotency.key.reused", "Idempotency-Key was already used with a different payload."));
        }

        return await ReplayAsync(store, winner.ResourceId, consumerUserId, cancellationToken);
    }

    public static async Task<Result<ConsumerOrderDto>> ReplayAsync(
        IOrderStore store,
        Guid? resourceId,
        Guid consumerUserId,
        CancellationToken cancellationToken)
    {
        if (resourceId is null)
        {
            return Result.Failure<ConsumerOrderDto>(
                Error.Conflict(ErrorCodes.Conflict, "Idempotent operation is missing its resource reference."));
        }

        var order = await store.ReadOrderAsync(resourceId.Value, cancellationToken);
        if (order is null || !order.BelongsToConsumer(consumerUserId))
        {
            return Result.Failure<ConsumerOrderDto>(Error.NotFound(ErrorCodes.NotFound, "Order not found."));
        }

        return Result.Success(OrderMapping.ToConsumerDto(order));
    }
}
