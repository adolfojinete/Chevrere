using System.Net;
using System.Net.Http.Json;
using Chevrere.Infrastructure.Audit;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Catalog.Application.Contracts;
using Chevrere.Modules.Inventory.Domain;
using Chevrere.Modules.Orders.Application.Abstractions;
using Chevrere.Modules.Orders.Application.Commands;
using Chevrere.Modules.Orders.Application.Contracts;
using Chevrere.Modules.Orders.Application.Idempotency;
using Chevrere.Modules.Orders.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Idempotency;
using Chevrere.SharedKernel.Inventory;
using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Results;
using Chevrere.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class OrdersConcurrencyTests(ChevrereApiFactory factory)
{
    [Fact]
    public async Task Cancel_versus_expire_leaves_one_terminal_state_and_one_release()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OCC1", "930700201", 4.4100d, -74.4100d, stock: 5);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-occ1@example.com", "Cons OCC1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "occ1-ord-00001");

        var cancelTask = client.PostAsJsonAsync(
            $"/api/v1/consumer/orders/{order.Id}/cancel", new CancelOrderRequest("race"));
        var expireTask = ExpireAsync(order.Id);
        await Task.WhenAll(cancelTask, expireTask);

        var latest = await client.GetFromJsonAsync<ConsumerOrderDto>(
            $"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json);
        Assert.True(latest!.Status is OrderStatus.Cancelled or OrderStatus.Expired);
        Assert.Equal((5L, 0L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));
        Assert.Equal(1, await CountMovementsAsync(seed.Store.StoreId, seed.ProductId, InventoryMovementType.ReservationReleased));
    }

    [Fact]
    public async Task Loser_same_key_create_recovery_cleans_same_dbcontext_without_global_detach()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OCC2", "930700202", 4.4200d, -74.4200d);
        var (consumerId, consumer) = await OrdersTestData.CreateConsumerAsync(
            factory, "cons-occ2@example.com", "Cons OCC2");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        const string key = "occ2-ord-00001";
        var product = await admin.GetFromJsonAsync<GlobalProductDto>(
            $"/api/v1/admin/products/{seed.ProductId}", AuthHelper.Json);

        await using var loserScope = factory.Services.CreateAsyncScope();
        loserScope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        loserScope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set(Guid.CreateVersion7().ToString("D"));
        var loserDb = loserScope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        var store = loserScope.ServiceProvider.GetRequiredService<IOrderStore>();
        var numbers = loserScope.ServiceProvider.GetRequiredService<IOrderNumberGenerator>();
        var reservations = loserScope.ServiceProvider.GetRequiredService<IInventoryReservationService>();
        var idempotency = loserScope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var audit = loserScope.ServiceProvider.GetRequiredService<IAuditRecorder>();
        var unitOfWork = loserScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = loserScope.ServiceProvider.GetRequiredService<IClock>();
        var policy = loserScope.ServiceProvider.GetRequiredService<IOrderReservationPolicy>();

        var keptCategory = await loserDb.Categories.SingleAsync(c => c.Id == product!.CategoryId);
        keptCategory.Update("Categoría OCC2 Kept", null, 4, clock.UtcNow);
        Assert.Equal(EntityState.Modified, loserDb.Entry(keptCategory).State);

        var attempt = await StageCreateAsync(
            store, numbers, reservations, idempotency, audit, clock, policy,
            consumerId, seed.Store.TenantId, seed.Store.StoreId, key);

        Assert.Equal(EntityState.Modified, loserDb.Entry(attempt.Cart).State);
        Assert.Equal(EntityState.Added, loserDb.Entry(attempt.Order).State);
        var stagedItem = attempt.Order.Items[0];
        Assert.Equal(EntityState.Added, loserDb.Entry(stagedItem).State);
        Assert.Equal(EntityState.Added, loserDb.Entry(attempt.Idempotency).State);
        Assert.Equal(1, loserDb.ChangeTracker.Entries<AuditEvent>().Count(e => e.State == EntityState.Added));
        Assert.Equal(1, loserDb.ChangeTracker.Entries<InventoryReservation>().Count(e => e.State == EntityState.Added));
        Assert.Equal(1, loserDb.ChangeTracker.Entries<InventoryMovement>().Count(e => e.State == EntityState.Added));

        var winner = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, key);
        await Assert.ThrowsAnyAsync<Exception>(() => unitOfWork.SaveChangesAsync());

        var replay = await OrderIdempotencyReplay.TryReplayAsync(
            store,
            idempotency,
            audit,
            reservations,
            attempt,
            seed.Store.TenantId,
            consumerId,
            key,
            attempt.Idempotency.RequestHash,
            CancellationToken.None);

        Assert.NotNull(replay);
        Assert.True(replay.IsSuccess);
        Assert.Equal(winner.Id, replay.Value.Id);

        Assert.Equal(EntityState.Detached, loserDb.Entry(attempt.Order).State);
        Assert.Equal(EntityState.Detached, loserDb.Entry(stagedItem).State);
        Assert.Equal(EntityState.Detached, loserDb.Entry(attempt.Cart).State);
        Assert.Equal(EntityState.Detached, loserDb.Entry(attempt.Idempotency).State);
        Assert.Equal(0, loserDb.ChangeTracker.Entries<AuditEvent>().Count(e => e.State == EntityState.Added));
        Assert.Equal(0, loserDb.ChangeTracker.Entries<InventoryReservation>().Count(e => e.State == EntityState.Added));
        Assert.Equal(0, loserDb.ChangeTracker.Entries<InventoryMovement>().Count(e => e.State == EntityState.Added));
        Assert.Equal(EntityState.Modified, loserDb.Entry(keptCategory).State);

        await unitOfWork.SaveChangesAsync();

        Assert.Equal("Categoría OCC2 Kept", await loserDb.Categories
            .Where(c => c.Id == keptCategory.Id)
            .Select(c => c.Name)
            .SingleAsync());
        Assert.Equal(winner.Id, (await client.GetFromJsonAsync<ConsumerOrderDto>(
            $"/api/v1/consumer/orders/{winner.Id}", AuthHelper.Json))!.Id);
        Assert.Equal((10L, 1L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));
        Assert.Equal(1, await CountOrdersAsync(seed.Store.StoreId));
        Assert.Equal(1, await CountMovementsAsync(seed.Store.StoreId, seed.ProductId, InventoryMovementType.Reservation));
    }

    private static async Task<CreateOrderAttempt> StageCreateAsync(
        IOrderStore store,
        IOrderNumberGenerator numbers,
        IInventoryReservationService reservations,
        IIdempotencyStore idempotency,
        IAuditRecorder audit,
        IClock clock,
        IOrderReservationPolicy policy,
        Guid consumerId,
        Guid tenantId,
        Guid storeId,
        string key)
    {
        var cart = await store.GetActiveCartAsync(consumerId, CancellationToken.None);
        Assert.NotNull(cart);
        var hash = OrderFingerprints.ForCreate(
            consumerId, cart.Id, store.GetCartVersion(cart), cart.TenantId, cart.StoreId, cart.Items);

        cart.Convert(clock.UtcNow);
        var order = Order.Place(
            await numbers.NextOrderNumberAsync(CancellationToken.None),
            consumerId,
            tenantId,
            storeId,
            cart.Id,
            [.. cart.Items.Select(i => new OrderLineSnapshot(
                i.GlobalProductId, "SKU", "Name", "Brand", "1 L", i.Quantity,
                Chevrere.SharedKernel.Domain.ValueObjects.Money.Create(6500m, "COP")))],
            policy.ReservationTtl,
            clock.UtcNow);
        store.AddOrder(order);

        var reserved = await reservations.ReserveAsync(
            new InventoryReservationRequest(
                order.TenantId,
                order.StoreId,
                consumerId,
                Guid.CreateVersion7().ToString("D"),
                order.ExpiresAt,
                [.. order.Items.Select(i => new InventoryReservationLine(
                    i.GlobalProductId, i.Quantity, InventoryReferenceTypes.OrderItem, i.Id))]),
            CancellationToken.None);

        var operation = new IdempotentOperation
        {
            Id = Guid.CreateVersion7(),
            TenantId = order.TenantId,
            Operation = IdempotencyOperations.OrdersCreate,
            IdempotencyKey = key,
            RequestHash = hash,
            ResourceId = order.Id,
            CreatedAt = clock.UtcNow
        };
        idempotency.Add(operation);
        audit.Record(AuditActions.OrderCreated, nameof(Order), order.Id, order.TenantId);
        return new CreateOrderAttempt(order, cart, reserved, operation);
    }

    private async Task ExpireAsync(Guid orderId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set(Guid.CreateVersion7().ToString("D"));
        var handler = scope.ServiceProvider.GetRequiredService<IHandler<ExpireOrderCommand, Result>>();
        var result = await handler.HandleAsync(new ExpireOrderCommand(orderId), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    private async Task<(long OnHand, long Reserved)> BalancesAsync(Guid storeId, Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        var item = await db.InventoryItems.IgnoreQueryFilters()
            .SingleAsync(i => i.StoreId == storeId && i.GlobalProductId == productId);
        return (item.OnHand, item.Reserved);
    }

    private async Task<int> CountMovementsAsync(Guid storeId, Guid productId, InventoryMovementType type)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.InventoryMovements.IgnoreQueryFilters().CountAsync(
            m => m.StoreId == storeId && m.GlobalProductId == productId && m.Type == type);
    }

    private async Task<int> CountOrdersAsync(Guid storeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.Orders.IgnoreQueryFilters().CountAsync(o => o.StoreId == storeId);
    }
}
