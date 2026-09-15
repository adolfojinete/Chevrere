using YaaJuu.Modules.Orders.Application.Abstractions;
using YaaJuu.Modules.Orders.Application.Contracts;
using YaaJuu.Modules.Orders.Domain;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Discovery;
using YaaJuu.SharedKernel.Domain;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using YaaJuu.SharedKernel.Time;

namespace YaaJuu.Modules.Orders.Application.Commands;

internal static class OrderLocation
{
    public static bool TryCreate(double latitude, double longitude, out Error error)
    {
        if (double.IsNaN(latitude) || double.IsInfinity(latitude) || latitude is < -90 or > 90)
        {
            error = Error.Validation("geo.latitude.invalid", "Latitude must be between -90 and 90.");
            return false;
        }

        if (double.IsNaN(longitude) || double.IsInfinity(longitude) || longitude is < -180 or > 180)
        {
            error = Error.Validation("geo.longitude.invalid", "Longitude must be between -180 and 180.");
            return false;
        }

        error = null!;
        return true;
    }
}

internal static class OrderConsumer
{
    public static Result<Guid> RequireUser(ICurrentUser currentUser)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Result.Failure<Guid>(Error.Unauthorized(ErrorCodes.Unauthorized, "The current user is not authenticated."));
        }

        return Result.Success(userId);
    }
}

internal static class CartProjection
{
    public static async Task<CartViewDto> BuildAsync(
        Cart? cart,
        ConsumerFulfillmentStore? store,
        IOrderCommercialReadStore commercial,
        CancellationToken cancellationToken)
    {
        var serviceAvailable = store is not null;
        var fulfillmentChanged = cart is not null
            && (store is null || store.StoreId != cart.StoreId || store.TenantId != cart.TenantId)
            && cart.Items.Count > 0;

        if (cart is null)
        {
            return new CartViewDto(null, null, serviceAvailable, false, false, []);
        }

        var commercialRows = cart.Items.Count == 0 || store is null || fulfillmentChanged
            ? []
            : await commercial.GetProductsAsync(
                cart.TenantId,
                cart.StoreId,
                [.. cart.Items.Select(i => i.GlobalProductId)],
                cancellationToken);
        var byProduct = commercialRows.ToDictionary(p => p.GlobalProductId);

        var items = cart.Items
            .Select(item =>
            {
                var available = byProduct.TryGetValue(item.GlobalProductId, out var row)
                    && item.Quantity <= row.Available;
                return new CartItemViewDto(
                    item.GlobalProductId,
                    item.Quantity,
                    available ? row!.Amount : row?.Amount,
                    available ? row!.Currency : row?.Currency,
                    available);
            })
            .ToList();

        var canCreate = serviceAvailable
            && !fulfillmentChanged
            && cart.Status == CartStatus.Active
            && items.Count > 0
            && items.TrueForAll(i => i.IsAvailable);

        return new CartViewDto(cart.Id, cart.Status, serviceAvailable, fulfillmentChanged, canCreate, items);
    }
}

public sealed record ViewCartQuery(double Latitude, double Longitude);

public sealed class ViewCartHandler(
    IOrderStore store,
    IOrderCommercialReadStore commercial,
    IConsumerStoreResolver resolver,
    ICurrentUser currentUser,
    IClock clock,
    IUnitOfWork unitOfWork)
    : IHandler<ViewCartQuery, Result<CartViewDto>>
{
    public async Task<Result<CartViewDto>> HandleAsync(ViewCartQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = OrderConsumer.RequireUser(currentUser);
        if (!user.IsSuccess)
        {
            return Result.Failure<CartViewDto>(user.Error!);
        }

        if (!OrderLocation.TryCreate(request.Latitude, request.Longitude, out var error))
        {
            return Result.Failure<CartViewDto>(error);
        }

        var fulfillment = await resolver.ResolveEligibleStoreAsync(
            request.Latitude, request.Longitude, cancellationToken);
        var cart = await store.GetActiveCartAsync(user.Value, cancellationToken);

        if (cart is not null && cart.Items.Count == 0 && fulfillment is not null
            && (cart.StoreId != fulfillment.StoreId || cart.TenantId != fulfillment.TenantId))
        {
            cart.RelocateEmpty(fulfillment.TenantId, fulfillment.StoreId, clock.UtcNow);
            store.PrepareCartForSave(cart);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(await CartProjection.BuildAsync(cart, fulfillment, commercial, cancellationToken));
    }
}

public sealed record PutCartItemCommand(Guid ProductId, double Latitude, double Longitude, long Quantity);

public sealed class PutCartItemHandler(
    IOrderStore store,
    IOrderCommercialReadStore commercial,
    IConsumerStoreResolver resolver,
    ICurrentUser currentUser,
    IClock clock,
    IUnitOfWork unitOfWork)
    : IHandler<PutCartItemCommand, Result<CartViewDto>>
{
    public async Task<Result<CartViewDto>> HandleAsync(PutCartItemCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = OrderConsumer.RequireUser(currentUser);
        if (!user.IsSuccess)
        {
            return Result.Failure<CartViewDto>(user.Error!);
        }

        if (!OrderLocation.TryCreate(request.Latitude, request.Longitude, out var error))
        {
            return Result.Failure<CartViewDto>(error);
        }

        var fulfillment = await resolver.ResolveEligibleStoreAsync(
            request.Latitude, request.Longitude, cancellationToken);
        if (fulfillment is null)
        {
            return Result.Failure<CartViewDto>(
                Error.Conflict("cart.no_service", "There is no delivery coverage at this location."));
        }

        var cart = await store.GetActiveCartAsync(user.Value, cancellationToken);
        if (cart is null)
        {
            cart = Cart.Start(user.Value, fulfillment.TenantId, fulfillment.StoreId, clock.UtcNow);
            store.AddCart(cart);
        }
        else if (cart.StoreId != fulfillment.StoreId || cart.TenantId != fulfillment.TenantId)
        {
            if (cart.Items.Count > 0)
            {
                return Result.Failure<CartViewDto>(
                    Error.Conflict("cart.fulfillment_changed", "The cart belongs to a different fulfillment store."));
            }

            cart.RelocateEmpty(fulfillment.TenantId, fulfillment.StoreId, clock.UtcNow);
        }

        var products = await commercial.GetProductsAsync(
            cart.TenantId, cart.StoreId, [request.ProductId], cancellationToken);
        var product = products.FirstOrDefault(p => p.GlobalProductId == request.ProductId);
        if (product is null)
        {
            return Result.Failure<CartViewDto>(
                Error.Conflict("cart.item_unavailable", "This product is not available at the current store."));
        }

        if (request.Quantity > product.Available)
        {
            return Result.Failure<CartViewDto>(
                Error.Conflict("order.insufficient_inventory", "There is not enough available stock for this quantity."));
        }

        try
        {
            cart.SetItem(request.ProductId, request.Quantity, clock.UtcNow);
            store.PrepareCartForSave(cart);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result.Failure<CartViewDto>(Error.Conflict(ex.Code, ex.Message));
        }
        catch (DuplicateKeyException)
        {
            return Result.Failure<CartViewDto>(Error.Conflict("cart.duplicate", "The cart could not be updated because of a unique constraint."));
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure<CartViewDto>(ConcurrencyConflictException.ToError());
        }

        return Result.Success(await CartProjection.BuildAsync(cart, fulfillment, commercial, cancellationToken));
    }
}

public sealed record RemoveCartItemCommand(Guid ProductId);

public sealed class RemoveCartItemHandler(
    IOrderStore store,
    IOrderCommercialReadStore commercial,
    ICurrentUser currentUser,
    IClock clock,
    IUnitOfWork unitOfWork)
    : IHandler<RemoveCartItemCommand, Result<CartViewDto>>
{
    public async Task<Result<CartViewDto>> HandleAsync(RemoveCartItemCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = OrderConsumer.RequireUser(currentUser);
        if (!user.IsSuccess)
        {
            return Result.Failure<CartViewDto>(user.Error!);
        }

        var cart = await store.GetActiveCartAsync(user.Value, cancellationToken);
        if (cart is null)
        {
            return Result.Success(new CartViewDto(null, null, false, false, false, []));
        }

        try
        {
            if (cart.RemoveItem(request.ProductId, clock.UtcNow))
            {
                store.PrepareCartForSave(cart);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure<CartViewDto>(Error.Conflict(ex.Code, ex.Message));
        }

        return Result.Success(await CartProjection.BuildAsync(cart, FulfillmentOf(cart), commercial, cancellationToken));
    }

    private static ConsumerFulfillmentStore FulfillmentOf(Cart cart) =>
        new(cart.StoreId, cart.TenantId, Guid.Empty);
}

public sealed record ClearCartCommand;

public sealed class ClearCartHandler(
    IOrderStore store,
    IOrderCommercialReadStore commercial,
    ICurrentUser currentUser,
    IClock clock,
    IUnitOfWork unitOfWork)
    : IHandler<ClearCartCommand, Result<CartViewDto>>
{
    public async Task<Result<CartViewDto>> HandleAsync(ClearCartCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = OrderConsumer.RequireUser(currentUser);
        if (!user.IsSuccess)
        {
            return Result.Failure<CartViewDto>(user.Error!);
        }

        var cart = await store.GetActiveCartAsync(user.Value, cancellationToken);
        if (cart is null)
        {
            return Result.Success(new CartViewDto(null, null, false, false, false, []));
        }

        try
        {
            if (cart.Clear(clock.UtcNow))
            {
                store.PrepareCartForSave(cart);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure<CartViewDto>(Error.Conflict(ex.Code, ex.Message));
        }

        return Result.Success(await CartProjection.BuildAsync(
            cart, new ConsumerFulfillmentStore(cart.StoreId, cart.TenantId, Guid.Empty), commercial, cancellationToken));
    }
}
