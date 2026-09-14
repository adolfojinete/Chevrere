using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Orders.Domain;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Chevrere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class OrdersIntegrityTests(ChevrereApiFactory factory)
{
    [Fact]
    public async Task Database_rejects_two_active_carts_for_the_same_consumer()
    {
        var ctx = await SeedAsync("OIX1", "930700101", 4.3100d, -74.3100d);
        await using var scope = Scope();
        var db = Db(scope);
        var cart = await db.Carts.IgnoreQueryFilters()
            .SingleAsync(c => c.ConsumerUserId == ctx.ConsumerId && c.Status == CartStatus.Active);

        db.Carts.Add(Cart.Start(ctx.ConsumerId, cart.TenantId, cart.StoreId, DateTimeOffset.UtcNow));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        AssertConstraint(ex, "ix_carts_consumer_active");
    }

    [Fact]
    public async Task Database_rejects_two_orders_from_the_same_cart()
    {
        var ctx = await SeedOrderAsync("OIX2", "930700102", 4.3200d, -74.3200d);
        await using var scope = Scope();
        var db = Db(scope);
        var order = await db.Orders.IgnoreQueryFilters().Include(o => o.Items).SingleAsync(o => o.Id == ctx.OrderId);
        var snapshot = order.Items.Select(i => new OrderLineSnapshot(
            i.GlobalProductId, i.Sku, i.Name, i.Brand, i.Presentation, i.Quantity, i.UnitPrice())).ToList();

        db.Orders.Add(Order.Place(
            "ORD-ROGUE001",
            order.ConsumerUserId,
            order.TenantId,
            order.StoreId,
            order.SourceCartId,
            snapshot,
            TimeSpan.FromMinutes(15),
            DateTimeOffset.UtcNow));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        AssertConstraint(ex, "ix_orders_source_cart_id");
    }

    [Fact]
    public async Task Database_rejects_duplicate_cart_and_order_products()
    {
        var ctx = await SeedAsync("OIX3", "930700103", 4.3300d, -74.3300d);
        await using var scope = Scope();
        var db = Db(scope);
        var cart = await db.Carts.IgnoreQueryFilters().Include(c => c.Items)
            .SingleAsync(c => c.ConsumerUserId == ctx.ConsumerId);
        var item = Assert.Single(cart.Items);

        await AssertConstraintSqlAsync(
            db,
            $"""
            INSERT INTO cart_items (id, cart_id, tenant_id, store_id, global_product_id, quantity, created_at, updated_at)
            VALUES ('{Guid.CreateVersion7()}', '{cart.Id}', '{cart.TenantId}', '{cart.StoreId}', '{item.GlobalProductId}', 1, TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00')
            """,
            "ix_cart_items_cart_product");

        var orderCtx = await SeedOrderAsync("OIX3B", "930700113", 4.3310d, -74.3310d);
        await using var orderScope = Scope();
        var orderDb = Db(orderScope);
        var line = await orderDb.OrderItems.IgnoreQueryFilters().SingleAsync(i => i.OrderId == orderCtx.OrderId);
        await AssertConstraintSqlAsync(
            orderDb,
            $"""
            INSERT INTO order_items (id, order_id, tenant_id, store_id, global_product_id, sku, name, brand, presentation, quantity, unit_price_amount, unit_price_currency, line_total_amount, created_at, updated_at)
            VALUES ('{Guid.CreateVersion7()}', '{line.OrderId}', '{line.TenantId}', '{line.StoreId}', '{line.GlobalProductId}', 'SKU', 'Name', 'Brand', '1 L', 1, 6500, 'COP', 6500, TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00')
            """,
            "ix_order_items_order_product");
    }

    [Fact]
    public async Task Database_rejects_non_positive_quantities_and_duplicate_reservation_references()
    {
        var ctx = await SeedOrderAsync("OIX4", "930700104", 4.3400d, -74.3400d);
        await using var scope = Scope();
        var db = Db(scope);
        var reservation = await db.InventoryReservations.IgnoreQueryFilters()
            .SingleAsync(r => r.StoreId == ctx.StoreId);

        await AssertConstraintSqlAsync(
            db,
            $"""
            INSERT INTO inventory_reservations (id, tenant_id, store_id, inventory_item_id, global_product_id, reference_type, reference_id, quantity, status, created_at, updated_at)
            VALUES ('{Guid.CreateVersion7()}', '{reservation.TenantId}', '{reservation.StoreId}', '{reservation.InventoryItemId}', '{reservation.GlobalProductId}', 'OrderItem', '{Guid.CreateVersion7()}', 0, 'Active', TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00')
            """,
            "ck_inventory_reservations_quantity_positive");

        await AssertConstraintSqlAsync(
            db,
            $"""
            INSERT INTO inventory_reservations (id, tenant_id, store_id, inventory_item_id, global_product_id, reference_type, reference_id, quantity, status, created_at, updated_at)
            VALUES ('{Guid.CreateVersion7()}', '{reservation.TenantId}', '{reservation.StoreId}', '{reservation.InventoryItemId}', '{reservation.GlobalProductId}', '{reservation.ReferenceType}', '{reservation.ReferenceId}', 1, 'Active', TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00')
            """,
            "ix_inventory_reservations_reference");
    }

    [Fact]
    public async Task Database_rejects_reservation_that_does_not_match_the_inventory_item()
    {
        var ctx = await SeedOrderAsync("OIX5", "930700105", 4.3500d, -74.3500d);
        await using var scope = Scope();
        var db = Db(scope);
        var reservation = await db.InventoryReservations.IgnoreQueryFilters()
            .SingleAsync(r => r.StoreId == ctx.StoreId);
        var otherProduct = Guid.CreateVersion7();

        await AssertConstraintSqlAsync(
            db,
            $"""
            INSERT INTO inventory_reservations (id, tenant_id, store_id, inventory_item_id, global_product_id, reference_type, reference_id, quantity, status, created_at, updated_at)
            VALUES ('{Guid.CreateVersion7()}', '{reservation.TenantId}', '{reservation.StoreId}', '{reservation.InventoryItemId}', '{otherProduct}', 'OrderItem', '{Guid.CreateVersion7()}', 1, 'Active', TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00')
            """,
            "fk_inventory_reservations_inventory_items_inventory_item_id_te");
    }

    private async Task<(Guid ConsumerId, Guid StoreId)> SeedAsync(
        string suffix, string identification, double lat, double lon)
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(factory, admin, suffix, identification, lat, lon);
        var (consumerId, consumer) = await OrdersTestData.CreateConsumerAsync(
            factory, $"cons-{suffix.ToLowerInvariant()}@example.com", $"Cons {suffix}");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        return (consumerId, seed.Store.StoreId);
    }

    private async Task<(Guid OrderId, Guid StoreId)> SeedOrderAsync(
        string suffix, string identification, double lat, double lon)
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(factory, admin, suffix, identification, lat, lon);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(
            factory, $"cons-{suffix.ToLowerInvariant()}@example.com", $"Cons {suffix}");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(
            client, seed.Latitude, seed.Longitude, $"{suffix.ToLowerInvariant()}-ord-00001");
        return (order.Id, seed.Store.StoreId);
    }

    private AsyncServiceScope Scope()
    {
        var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        return scope;
    }

    private static ChevrereDbContext Db(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();

    private static void AssertConstraint(DbUpdateException ex, string name)
    {
        Assert.NotNull(ex.InnerException);
        Assert.Contains(name, ex.InnerException.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertConstraintSqlAsync(ChevrereDbContext db, string sql, string name)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.Contains(name, ex.ConstraintName ?? ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
