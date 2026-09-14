using Chevrere.Modules.Orders.Application.Abstractions;
using Chevrere.Modules.Orders.Application.Contracts;
using Chevrere.Modules.Orders.Application.Idempotency;
using Chevrere.Modules.Orders.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Discovery;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Idempotency;
using Chevrere.SharedKernel.Inventory;
using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Results;
using Chevrere.SharedKernel.Time;

namespace Chevrere.Modules.Orders.Application.Commands;

public sealed record CreateOrderCommand(double Latitude, double Longitude, string IdempotencyKey);

public sealed class CreateOrderHandler(
    IOrderStore store,
    IOrderCommercialReadStore commercial,
    IConsumerStoreResolver resolver,
    IOrderNumberGenerator numbers,
    IInventoryReservationService reservations,
    IIdempotencyStore idempotency,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOrderReservationPolicy reservationPolicy)
    : IHandler<CreateOrderCommand, Result<ConsumerOrderDto>>
{
    public async Task<Result<ConsumerOrderDto>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IdempotencyKeyRules.IsValid(request.IdempotencyKey))
        {
            return Result.Failure<ConsumerOrderDto>(Error.Validation(
                "orders.idempotency_key.required",
                "Idempotency-Key is required (8-128 chars, no whitespace)."));
        }

        var user = OrderConsumer.RequireUser(currentUser);
        if (!user.IsSuccess)
        {
            return Result.Failure<ConsumerOrderDto>(user.Error!);
        }

        if (!OrderLocation.TryCreate(request.Latitude, request.Longitude, out var locationError))
        {
            return Result.Failure<ConsumerOrderDto>(locationError);
        }

        var fulfillment = await resolver.ResolveEligibleStoreAsync(
            request.Latitude, request.Longitude, cancellationToken);
        if (fulfillment is null)
        {
            return Result.Failure<ConsumerOrderDto>(
                Error.Conflict("order.no_service_at_location", "There is no delivery coverage at this location."));
        }

        var existingKey = await idempotency.FindAsync(
            fulfillment.TenantId, IdempotencyOperations.OrdersCreate, request.IdempotencyKey, cancellationToken);
        var cart = await store.GetActiveCartAsync(user.Value, cancellationToken);

        if (existingKey is not null)
        {
            if (cart is not null && cart.Items.Count > 0)
            {
                var replayHash = OrderFingerprints.ForCreate(
                    user.Value, cart.Id, store.GetCartVersion(cart), cart.TenantId, cart.StoreId, cart.Items);
                if (!string.Equals(existingKey.RequestHash, replayHash, StringComparison.Ordinal))
                {
                    return Result.Failure<ConsumerOrderDto>(
                        Error.Conflict("idempotency.key.reused", "Idempotency-Key was already used with a different payload."));
                }
            }

            return await OrderIdempotencyReplay.ReplayAsync(store, existingKey.ResourceId, user.Value, cancellationToken);
        }

        if (cart is null || cart.Items.Count == 0)
        {
            return Result.Failure<ConsumerOrderDto>(Error.Conflict("cart.empty", "The cart is empty."));
        }

        if (cart.StoreId != fulfillment.StoreId || cart.TenantId != fulfillment.TenantId)
        {
            return Result.Failure<ConsumerOrderDto>(
                Error.Conflict("cart.fulfillment_changed", "The cart belongs to a different fulfillment store."));
        }

        var hash = OrderFingerprints.ForCreate(
            user.Value, cart.Id, store.GetCartVersion(cart), cart.TenantId, cart.StoreId, cart.Items);

        var commercialRows = await commercial.GetProductsAsync(
            cart.TenantId,
            cart.StoreId,
            [.. cart.Items.Select(i => i.GlobalProductId)],
            cancellationToken);
        var byProduct = commercialRows.ToDictionary(p => p.GlobalProductId);
        foreach (var item in cart.Items)
        {
            if (!byProduct.TryGetValue(item.GlobalProductId, out var row) || item.Quantity > row.Available)
            {
                return Result.Failure<ConsumerOrderDto>(
                    Error.Conflict("order.insufficient_inventory", "There is not enough available stock to place this order."));
            }
        }

        try
        {
            var snapshots = cart.Items
                .Select(item =>
                {
                    var row = byProduct[item.GlobalProductId];
                    return new OrderLineSnapshot(
                        item.GlobalProductId,
                        row.Sku,
                        row.Name,
                        row.Brand,
                        row.Presentation,
                        item.Quantity,
                        row.UnitPrice());
                })
                .ToList();

            cart.Convert(clock.UtcNow);
            var order = Order.Place(
                await numbers.NextOrderNumberAsync(cancellationToken),
                user.Value,
                cart.TenantId,
                cart.StoreId,
                cart.Id,
                snapshots,
                reservationPolicy.ReservationTtl,
                clock.UtcNow);
            store.AddOrder(order);

            var reserved = await reservations.ReserveAsync(
                new InventoryReservationRequest(
                    order.TenantId,
                    order.StoreId,
                    user.Value,
                    correlation.CorrelationId,
                    order.ExpiresAt,
                    [.. order.Items.Select(i => new InventoryReservationLine(
                        i.GlobalProductId,
                        i.Quantity,
                        InventoryReferenceTypes.OrderItem,
                        i.Id))]),
                cancellationToken);

            var operation = new IdempotentOperation
            {
                Id = Guid.CreateVersion7(),
                TenantId = order.TenantId,
                Operation = IdempotencyOperations.OrdersCreate,
                IdempotencyKey = request.IdempotencyKey,
                RequestHash = hash,
                ResourceId = order.Id,
                CreatedAt = clock.UtcNow
            };
            idempotency.Add(operation);

            audit.Record(
                AuditActions.OrderCreated,
                nameof(Order),
                order.Id,
                order.TenantId,
                previousValue: null,
                newValue: new
                {
                    order.Number,
                    order.SourceCartId,
                    order.StoreId,
                    Lines = order.Items.Select(i => new { i.GlobalProductId, i.Quantity, i.LineTotalAmount })
                });

            var attempt = new CreateOrderAttempt(order, cart, reserved, operation);
            try
            {
                store.PrepareCartForSave(cart);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is DuplicateKeyException or ConcurrencyConflictException)
            {
                var replay = await OrderIdempotencyReplay.TryReplayAsync(
                    store,
                    idempotency,
                    audit,
                    reservations,
                    attempt,
                    order.TenantId,
                    user.Value,
                    request.IdempotencyKey,
                    hash,
                    cancellationToken);
                if (replay is not null)
                {
                    return replay;
                }

                var latestCart = await store.GetCartAsync(attempt.Cart.Id, cancellationToken);
                if (latestCart?.Status == CartStatus.Converted || ex is DuplicateKeyException)
                {
                    return Result.Failure<ConsumerOrderDto>(
                        Error.Conflict("cart.already_converted", "This cart has already been converted into an order."));
                }

                return Result.Failure<ConsumerOrderDto>(
                    Error.Conflict("order.insufficient_inventory", "There is not enough available stock to place this order."));
            }

            return Result.Success(OrderMapping.ToConsumerDto(order));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ConsumerOrderDto>(MapDomain(ex));
        }
    }

    private static Error MapDomain(DomainException ex) =>
        ex.Code is "inventory.insufficient_available" or "inventory.reservation.quantity.invalid"
            ? Error.Conflict("order.insufficient_inventory", "There is not enough available stock to place this order.")
            : Error.Conflict(ex.Code, ex.Message);
}

public sealed record CancelOrderCommand(Guid OrderId, string? Reason);

public sealed class CancelOrderHandler(
    IOrderStore store,
    IInventoryReservationService reservations,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<CancelOrderCommand, Result<ConsumerOrderDto>>
{
    public async Task<Result<ConsumerOrderDto>> HandleAsync(
        CancelOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = OrderConsumer.RequireUser(currentUser);
        if (!user.IsSuccess)
        {
            return Result.Failure<ConsumerOrderDto>(user.Error!);
        }

        var order = await store.GetOrderForConsumerAsync(request.OrderId, user.Value, cancellationToken);
        if (order is null)
        {
            return Result.Failure<ConsumerOrderDto>(Error.NotFound(ErrorCodes.NotFound, "Order not found."));
        }

        try
        {
            if (order.Status == OrderStatus.Cancelled)
            {
                return Result.Success(OrderMapping.ToConsumerDto(order));
            }

            if (order.Status is OrderStatus.Confirmed or OrderStatus.Expired)
            {
                return Result.Failure<ConsumerOrderDto>(
                    Error.Conflict("order.cannot_cancel", "An order in a terminal state cannot be cancelled."));
            }

            await reservations.ReleaseAsync(
                new InventoryReservationReleaseRequest(
                    order.TenantId,
                    order.StoreId,
                    user.Value,
                    correlation.CorrelationId,
                    InventoryReferenceTypes.OrderItem,
                    [.. order.Items.Select(i => i.Id)]),
                cancellationToken);

            if (!order.Cancel(request.Reason, clock.UtcNow))
            {
                return Result.Success(OrderMapping.ToConsumerDto(order));
            }

            audit.Record(
                AuditActions.OrderCancelled,
                nameof(Order),
                order.Id,
                order.TenantId,
                previousValue: new { Status = OrderStatus.PendingPayment },
                newValue: new { order.Status, order.CancelReason });

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is DuplicateKeyException or ConcurrencyConflictException)
            {
                var latest = await store.GetOrderForConsumerAsync(request.OrderId, user.Value, cancellationToken);
                if (latest is null)
                {
                    return Result.Failure<ConsumerOrderDto>(Error.NotFound(ErrorCodes.NotFound, "Order not found."));
                }

                if (latest.Status == OrderStatus.Cancelled)
                {
                    return Result.Success(OrderMapping.ToConsumerDto(latest));
                }

                return Result.Failure<ConsumerOrderDto>(
                    latest.Status is OrderStatus.Expired or OrderStatus.Confirmed
                        ? Error.Conflict("order.cannot_cancel", "An order in a terminal state cannot be cancelled.")
                        : ConcurrencyConflictException.ToError());
            }

            return Result.Success(OrderMapping.ToConsumerDto(order));
        }
        catch (DomainException ex) when (InventoryReservationErrors.IsIntegrityFailure(ex.Code))
        {
            return Result.Failure<ConsumerOrderDto>(Error.Failure(ex.Code, ex.Message));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ConsumerOrderDto>(Error.Conflict(ex.Code, ex.Message));
        }
    }
}

public sealed record ExpireOrderCommand(Guid OrderId);

public sealed class ExpireOrderHandler(
    IOrderStore store,
    IInventoryReservationService reservations,
    ICorrelationContext correlation,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<ExpireOrderCommand, Result>
{
    public async Task<Result> HandleAsync(ExpireOrderCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var order = await store.GetOrderAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound(ErrorCodes.NotFound, "Order not found."));
        }

        try
        {
            if (order.Status != OrderStatus.PendingPayment)
            {
                return Result.Success();
            }

            await reservations.ReleaseAsync(
                new InventoryReservationReleaseRequest(
                    order.TenantId,
                    order.StoreId,
                    ActorUserId: null,
                    CorrelationOrNew(),
                    InventoryReferenceTypes.OrderItem,
                    [.. order.Items.Select(i => i.Id)]),
                cancellationToken);

            if (!order.Expire(clock.UtcNow))
            {
                return Result.Success();
            }

            audit.Record(
                AuditActions.OrderExpired,
                nameof(Order),
                order.Id,
                order.TenantId,
                previousValue: new { Status = OrderStatus.PendingPayment },
                newValue: new { order.Status });

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is DuplicateKeyException or ConcurrencyConflictException)
            {
                return Result.Success();
            }

            return Result.Success();
        }
        catch (DomainException ex) when (ex.Code == "order.cannot_expire")
        {
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Failure(ex.Code, ex.Message));
        }
    }

    private string CorrelationOrNew()
    {
        try
        {
            return correlation.CorrelationId;
        }
        catch (InvalidOperationException)
        {
            return Guid.CreateVersion7().ToString("D");
        }
    }
}
