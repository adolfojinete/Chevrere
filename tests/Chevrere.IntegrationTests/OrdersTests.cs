using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Catalog.Application.Contracts;
using Chevrere.Modules.Inventory.Application.Contracts;
using Chevrere.Modules.Inventory.Domain;
using Chevrere.Modules.Orders.Application.Commands;
using Chevrere.Modules.Orders.Application.Contracts;
using Chevrere.Modules.Orders.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Inventory;
using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class OrdersTests(ChevrereApiFactory factory)
{
    [Fact]
    public async Task Cart_add_does_not_reserve_stock()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OAD1", "930700001", 4.1000d, -74.1000d, stock: 8);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-oad1@example.com", "Cons OAD1");
        using var client = consumer;

        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 2);
        Assert.Equal((8L, 0L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));
    }

    [Fact]
    public async Task Create_order_reserves_stock_and_snapshots_price_and_name()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OAD2", "930700002", 4.1100d, -74.1100d, stock: 8, price: 6500m);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-oad2@example.com", "Cons OAD2");
        using var client = consumer;

        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 2);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "oad2-ord-00001");

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.StartsWith("ORD-", order.Number, StringComparison.Ordinal);
        Assert.Equal(13000m, order.TotalAmount);
        Assert.Equal(6500m, Assert.Single(order.Items).UnitPriceAmount);
        Assert.Equal("Producto OAD2", order.Items[0].Name);
        Assert.Equal((8L, 2L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));
        Assert.Equal(1, await CountMovementsAsync(seed.Store.StoreId, seed.ProductId, InventoryMovementType.Reservation));

        await ConsumerTestData.SetGlobalPriceAsync(admin, seed.ProductId, 7000m);
        var updated = await admin.PutAsJsonAsync(
            $"/api/v1/admin/products/{seed.ProductId}",
            new UpdateGlobalProductRequest(
                (await admin.GetFromJsonAsync<GlobalProductDto>($"/api/v1/admin/products/{seed.ProductId}", AuthHelper.Json))!.CategoryId,
                "Producto OAD2 Renombrado",
                "Marca",
                "1 L",
                null,
                null));
        updated.EnsureSuccessStatusCode();

        var reread = await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json);
        Assert.Equal(6500m, reread!.Items[0].UnitPriceAmount);
        Assert.Equal("Producto OAD2", reread.Items[0].Name);

        var cart = await (await OrdersTestData.ViewCartAsync(client, seed.Latitude, seed.Longitude))
            .Content.ReadFromJsonAsync<CartViewDto>(AuthHelper.Json);
        Assert.False(cart!.CanCreateOrder);
        Assert.Empty(cart.Items);
    }

    [Fact]
    public async Task Cart_view_shows_current_price_after_catalog_change()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OAD3", "930700003", 4.1200d, -74.1200d, stock: 5, price: 6500m);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-oad3@example.com", "Cons OAD3");
        using var client = consumer;

        var cart = await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        Assert.Equal(6500m, cart.Items[0].CurrentUnitPrice);
        await ConsumerTestData.SetGlobalPriceAsync(admin, seed.ProductId, 7000m);
        var viewed = await (await OrdersTestData.ViewCartAsync(client, seed.Latitude, seed.Longitude))
            .Content.ReadFromJsonAsync<CartViewDto>(AuthHelper.Json);
        Assert.Equal(7000m, viewed!.Items[0].CurrentUnitPrice);
        Assert.True(viewed.CanCreateOrder);
    }

    [Fact]
    public async Task Multi_item_create_fails_atomically_when_one_line_is_unavailable()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OAD4", "930700004", 4.1300d, -74.1300d, stock: 1);
        var extra = await CatalogAdminTests.CreateProductAsync(
            admin,
            (await admin.GetFromJsonAsync<GlobalProductDto>($"/api/v1/admin/products/{seed.ProductId}", AuthHelper.Json))!.CategoryId,
            "SKU-OAD4B",
            "Producto OAD4B",
            null);
        await ConsumerTestData.SetGlobalPriceAsync(admin, extra.Id, 5000m);
        using var owner = await ConsumerTestData.OwnerClientAsync(factory, "owner-oad4@example.com");
        await ConsumerTestData.StockAndPriceAsync(owner, seed.Store.StoreId, extra.Id, 1, "oad4-extra-01");

        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-oad4@example.com", "Cons OAD4");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        await OrdersTestData.PutItemBodyAsync(client, extra.Id, seed.Latitude, seed.Longitude, 1);

        (await owner.PostAsync($"/api/v1/business/stores/{seed.Store.StoreId}/products/{extra.Id}/disable", null))
            .EnsureSuccessStatusCode();

        var response = await OrdersTestData.CreateOrderAsync(client, seed.Latitude, seed.Longitude, "oad4-ord-00001");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal((1L, 0L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));
        Assert.Equal((1L, 0L), await BalancesAsync(seed.Store.StoreId, extra.Id));

        var cart = await (await OrdersTestData.ViewCartAsync(client, seed.Latitude, seed.Longitude))
            .Content.ReadFromJsonAsync<CartViewDto>(AuthHelper.Json);
        Assert.Equal(2, cart!.Items.Count);
        Assert.False(cart.CanCreateOrder);
    }

    [Fact]
    public async Task Same_key_replays_and_different_keys_on_same_cart_conflict()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OAD5", "930700005", 4.1400d, -74.1400d);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-oad5@example.com", "Cons OAD5");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);

        const string key = "oad5-ord-00001";
        var first = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, key);
        var replay = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, key);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(first.Number, replay.Number);

        var empty = await OrdersTestData.CreateOrderAsync(client, seed.Latitude, seed.Longitude, "oad5-ord-00002");
        Assert.Equal(HttpStatusCode.Conflict, empty.StatusCode);
        Assert.Equal(
            "cart.empty",
            (await empty.Content.ReadFromJsonAsync<ProblemDetails>(AuthHelper.Json))!.Title);

        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var reused = await OrdersTestData.CreateOrderAsync(client, seed.Latitude, seed.Longitude, key);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal(
            "idempotency.key.reused",
            (await reused.Content.ReadFromJsonAsync<ProblemDetails>(AuthHelper.Json))!.Title);
        Assert.Equal(1, await CountOrdersForConsumerAsync(first.Id, first.Number));
    }

    [Fact]
    public async Task Concurrent_different_keys_on_same_cart_create_one_order()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OAD5B", "930700015", 4.1410d, -74.1410d);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-oad5b@example.com", "Cons OAD5B");
        using var a = consumer;
        var b = factory.CreateClientUnredirected();
        b.DefaultRequestHeaders.Authorization = a.DefaultRequestHeaders.Authorization;
        await OrdersTestData.PutItemBodyAsync(a, seed.ProductId, seed.Latitude, seed.Longitude, 1);

        var responses = await Task.WhenAll(
            OrdersTestData.CreateOrderAsync(a, seed.Latitude, seed.Longitude, "oad5b-ord-a0001"),
            OrdersTestData.CreateOrderAsync(b, seed.Latitude, seed.Longitude, "oad5b-ord-b0001"));

        Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
        var conflict = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(
            "cart.already_converted",
            (await conflict.Content.ReadFromJsonAsync<ProblemDetails>(AuthHelper.Json))!.Title);
        Assert.Equal((10L, 1L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));
    }

    [Fact]
    public async Task Last_unit_two_consumers_only_one_succeeds()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OAD6", "930700006", 4.1500d, -74.1500d, stock: 1);
        var (_, a) = await OrdersTestData.CreateConsumerAsync(factory, "cons-oad6a@example.com", "Cons OAD6A");
        var (_, b) = await OrdersTestData.CreateConsumerAsync(factory, "cons-oad6b@example.com", "Cons OAD6B");
        using var clientA = a;
        using var clientB = b;
        await OrdersTestData.PutItemBodyAsync(clientA, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        await OrdersTestData.PutItemBodyAsync(clientB, seed.ProductId, seed.Latitude, seed.Longitude, 1);

        var responses = await Task.WhenAll(
            OrdersTestData.CreateOrderAsync(clientA, seed.Latitude, seed.Longitude, "oad6-ord-a0001"),
            OrdersTestData.CreateOrderAsync(clientB, seed.Latitude, seed.Longitude, "oad6-ord-b0001"));

        Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
        Assert.Contains(HttpStatusCode.Conflict, responses.Select(r => r.StatusCode));
        Assert.Equal((1L, 1L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));
    }

    [Fact]
    public async Task Cancel_releases_once_and_expire_handler_releases_pending_orders()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OAD7", "930700007", 4.1600d, -74.1600d, stock: 5);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-oad7@example.com", "Cons OAD7");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "oad7-ord-00001");
        Assert.Equal((5L, 1L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));

        var cancel = await client.PostAsJsonAsync($"/api/v1/consumer/orders/{order.Id}/cancel", new CancelOrderRequest("nope"));
        cancel.EnsureSuccessStatusCode();
        var cancelled = await cancel.Content.ReadFromJsonAsync<ConsumerOrderDto>(AuthHelper.Json);
        Assert.Equal(OrderStatus.Cancelled, cancelled!.Status);
        Assert.Equal((5L, 0L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));

        var again = await client.PostAsJsonAsync($"/api/v1/consumer/orders/{order.Id}/cancel", new CancelOrderRequest(null));
        again.EnsureSuccessStatusCode();
        Assert.Equal(1, await CountMovementsAsync(seed.Store.StoreId, seed.ProductId, InventoryMovementType.ReservationReleased));

        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var pending = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "oad7-ord-00002");
        await ExpireAsync(pending.Id);
        var expired = await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{pending.Id}", AuthHelper.Json);
        Assert.Equal(OrderStatus.Expired, expired!.Status);
        Assert.Equal((5L, 0L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));
    }

    [Fact]
    public async Task Ownership_privacy_and_anonymous_surfaces()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OAD8", "930700008", 4.1700d, -74.1700d);
        var (_, consumerA) = await OrdersTestData.CreateConsumerAsync(factory, "cons-oad8a@example.com", "Cons OAD8A");
        var (_, consumerB) = await OrdersTestData.CreateConsumerAsync(factory, "cons-oad8b@example.com", "Cons OAD8B");
        using var a = consumerA;
        using var b = consumerB;
        await OrdersTestData.PutItemBodyAsync(a, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(a, seed.Latitude, seed.Longitude, "oad8-ord-00001");

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/v1/consumer/orders/{order.Id}")).StatusCode);

        using var anonymous = factory.CreateClientUnredirected();
        Assert.Equal(HttpStatusCode.Unauthorized, (await OrdersTestData.ViewCartAsync(anonymous, seed.Latitude, seed.Longitude)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/consumer/orders")).StatusCode);
        var catalog = await ConsumerTestData.SearchAsync(anonymous, seed.Latitude, seed.Longitude);
        Assert.True(catalog.ServiceAvailable);

        var payload = await (await a.GetAsync($"/api/v1/consumer/orders/{order.Id}")).Content.ReadAsStringAsync();
        foreach (var field in new[] { "storeId", "tenantId", "latitude", "longitude", "reservation", "inventory", "cost", "supplier" })
        {
            Assert.DoesNotContain(field, payload, StringComparison.OrdinalIgnoreCase);
        }

        using var owner = await ConsumerTestData.OwnerClientAsync(factory, "owner-oad8@example.com");
        (await owner.GetAsync($"/api/v1/business/stores/{seed.Store.StoreId}/orders/{order.Id}")).EnsureSuccessStatusCode();

        var other = await ConsumerTestData.CreateActiveStoreAsync(admin, "OAD8X", "owner-oad8x@example.com", "930700018");
        using var foreign = await ConsumerTestData.OwnerClientAsync(factory, "owner-oad8x@example.com");
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await foreign.GetAsync($"/api/v1/business/stores/{other.StoreId}/orders/{order.Id}")).StatusCode);

        (await admin.GetAsync($"/api/v1/admin/orders/{order.Id}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Same_consumer_lists_orders_from_two_tenants()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var north = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OAD9A", "930700009", 4.1800d, -74.1800d);
        var south = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OAD9B", "930700019", 8.1800d, -75.1800d);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-oad9@example.com", "Cons OAD9");
        using var client = consumer;

        await OrdersTestData.PutItemBodyAsync(client, north.ProductId, north.Latitude, north.Longitude, 1);
        var first = await OrdersTestData.CreateOrderBodyAsync(client, north.Latitude, north.Longitude, "oad9-ord-00001");
        await OrdersTestData.PutItemBodyAsync(client, south.ProductId, south.Latitude, south.Longitude, 1);
        var second = await OrdersTestData.CreateOrderBodyAsync(client, south.Latitude, south.Longitude, "oad9-ord-00002");

        var list = await client.GetFromJsonAsync<PagedResult<ConsumerOrderSummaryDto>>(
            "/api/v1/consumer/orders?page=1&pageSize=20", AuthHelper.Json);
        Assert.Contains(list!.Items, i => i.Id == first.Id);
        Assert.Contains(list.Items, i => i.Id == second.Id);
    }

    [Fact]
    public async Task Waste_rejects_when_available_is_below_quantity_and_commit_drops_on_hand()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OADA", "930700010", 4.1900d, -74.1900d, stock: 10);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-oada@example.com", "Cons OADA");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 3);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "oada-ord-00001");
        Assert.Equal((10L, 3L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));

        using var owner = await ConsumerTestData.OwnerClientAsync(factory, "owner-oada@example.com");
        using var waste = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/business/stores/{seed.Store.StoreId}/inventory/{seed.ProductId}/waste")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new WasteInventoryRequest(8, "Merma excesiva"), AuthHelper.Json),
                Encoding.UTF8,
                "application/json")
        };
        waste.Headers.TryAddWithoutValidation("Idempotency-Key", "oada-waste-0001");
        var wasted = await owner.SendAsync(waste);
        Assert.Equal(HttpStatusCode.Conflict, wasted.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set(Guid.CreateVersion7().ToString("D"));
        var reservations = scope.ServiceProvider.GetRequiredService<IInventoryReservationService>();
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        var itemIds = await db.OrderItems.IgnoreQueryFilters()
            .Where(i => i.OrderId == order.Id)
            .Select(i => i.Id)
            .ToListAsync();
        await reservations.CommitAsync(
            new InventoryReservationReleaseRequest(
                seed.Store.TenantId,
                seed.Store.StoreId,
                null,
                Guid.CreateVersion7().ToString("D"),
                InventoryReferenceTypes.OrderItem,
                itemIds),
            CancellationToken.None);
        await db.SaveChangesAsync();
        Assert.Equal((7L, 0L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));
    }

    [Fact]
    public async Task Worker_is_disabled_so_past_expiry_stays_pending_until_handler()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OADB", "930700011", 4.2000d, -74.2000d);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-oadb@example.com", "Cons OADB");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "oadb-ord-00001");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
            var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
            await db.Orders.IgnoreQueryFilters()
                .Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, o => o.CreatedAt.AddMinutes(-1)));
        }

        await Task.Delay(200);
        var still = await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json);
        Assert.Equal(OrderStatus.PendingPayment, still!.Status);

        await ExpireAsync(order.Id);
        var expired = await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json);
        Assert.Equal(OrderStatus.Expired, expired!.Status);
    }

    [Fact]
    public async Task Concurrent_same_key_creates_one_order()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "OADC", "930700012", 4.2100d, -74.2100d);
        var (_, first) = await OrdersTestData.CreateConsumerAsync(factory, "cons-oadc@example.com", "Cons OADC");
        using var a = first;
        var b = factory.CreateClientUnredirected();
        b.DefaultRequestHeaders.Authorization = a.DefaultRequestHeaders.Authorization;
        await OrdersTestData.PutItemBodyAsync(a, seed.ProductId, seed.Latitude, seed.Longitude, 1);

        const string key = "oadc-ord-00001";
        var responses = await Task.WhenAll(
            OrdersTestData.CreateOrderAsync(a, seed.Latitude, seed.Longitude, key),
            OrdersTestData.CreateOrderAsync(b, seed.Latitude, seed.Longitude, key));
        Assert.All(responses, r => r.EnsureSuccessStatusCode());
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<ConsumerOrderDto>(AuthHelper.Json)));
        Assert.Equal(bodies[0]!.Id, bodies[1]!.Id);
        Assert.Equal((10L, 1L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));
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

    private async Task<int> CountOrdersForConsumerAsync(Guid orderId, string number)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.Orders.IgnoreQueryFilters().CountAsync(o => o.Id == orderId || o.Number == number);
    }
}
