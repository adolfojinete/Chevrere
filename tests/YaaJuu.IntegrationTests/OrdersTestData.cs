using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using YaaJuu.Modules.Catalog.Application.Contracts;
using YaaJuu.Modules.Identity.Application.Abstractions;
using YaaJuu.Modules.Orders.Application.Contracts;
using YaaJuu.Modules.Tenancy.Application.Contracts;
using YaaJuu.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.IntegrationTests;

internal static class OrdersTestData
{
    public const string ConsumerPassword = "Consumer!23456";

    public static async Task<(CreateFranchiseeResponse Store, Guid ProductId, double Latitude, double Longitude)> SeedBuyableStoreAsync(
        YaaJuuApiFactory factory,
        HttpClient admin,
        string suffix,
        string identification,
        double latitude,
        double longitude,
        long stock = 10,
        decimal price = 6500m)
    {
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, $"BEB-{suffix}", $"Bebidas {suffix}");
        var product = await CatalogAdminTests.CreateProductAsync(
            admin, category.Id, $"SKU-{suffix}", $"Producto {suffix}", null);
        await ConsumerTestData.SetGlobalPriceAsync(admin, product.Id, price);

        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, suffix, $"owner-{suffix.ToLowerInvariant()}@example.com", identification);
        await ConsumerTestData.OpenAreaAsync(admin, store.StoreId, latitude, longitude, 8000);
        using var owner = await ConsumerTestData.OwnerClientAsync(factory, $"owner-{suffix.ToLowerInvariant()}@example.com");
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, product.Id, stock, $"ord-{suffix}-init01");
        return (store, product.Id, latitude, longitude);
    }

    public static async Task<(CreateFranchiseeResponse Store, IReadOnlyList<Guid> ProductIds, double Latitude, double Longitude)> SeedThreeProductStoreAsync(
        YaaJuuApiFactory factory,
        HttpClient admin,
        string suffix,
        string identification,
        double latitude,
        double longitude)
    {
        var seed = await SeedBuyableStoreAsync(factory, admin, suffix, identification, latitude, longitude, stock: 10);
        var categoryId = (await admin.GetFromJsonAsync<GlobalProductDto>(
            $"/api/v1/admin/products/{seed.ProductId}", AuthHelper.Json))!.CategoryId;
        var second = await CatalogAdminTests.CreateProductAsync(
            admin, categoryId, $"SKU-{suffix}-B", $"Producto {suffix}B", null);
        var third = await CatalogAdminTests.CreateProductAsync(
            admin, categoryId, $"SKU-{suffix}-C", $"Producto {suffix}C", null);
        await ConsumerTestData.SetGlobalPriceAsync(admin, second.Id, 5000m);
        await ConsumerTestData.SetGlobalPriceAsync(admin, third.Id, 4000m);
        using var owner = await ConsumerTestData.OwnerClientAsync(
            factory, $"owner-{suffix.ToLowerInvariant()}@example.com");
        await ConsumerTestData.StockAndPriceAsync(owner, seed.Store.StoreId, second.Id, 10, $"ord-{suffix}-b-01");
        await ConsumerTestData.StockAndPriceAsync(owner, seed.Store.StoreId, third.Id, 10, $"ord-{suffix}-c-01");
        return (seed.Store, [seed.ProductId, second.Id, third.Id], latitude, longitude);
    }

    public static async Task<(Guid UserId, HttpClient Client)> CreateConsumerAsync(
        YaaJuuApiFactory factory,
        string email,
        string displayName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var provisioning = scope.ServiceProvider.GetRequiredService<IIdentityProvisioning>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var created = await provisioning.AddConsumerAsync(email, ConsumerPassword, displayName, CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        await unitOfWork.SaveChangesAsync();

        var client = factory.CreateClientUnredirected();
        var token = await AuthHelper.LoginAsync(client, email, ConsumerPassword);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return (created.Value.UserId, client);
    }

    public static Task<HttpResponseMessage> ViewCartAsync(HttpClient client, double latitude, double longitude) =>
        client.PostAsJsonAsync("/api/v1/consumer/cart/view", new OrderLocationRequest(latitude, longitude));

    public static Task<HttpResponseMessage> PutItemAsync(
        HttpClient client,
        Guid productId,
        double latitude,
        double longitude,
        long quantity) =>
        client.PutAsJsonAsync(
            $"/api/v1/consumer/cart/items/{productId}",
            new ConsumerLocationQuantityRequest(latitude, longitude, quantity));

    public static async Task<CartViewDto> PutItemBodyAsync(
        HttpClient client,
        Guid productId,
        double latitude,
        double longitude,
        long quantity)
    {
        var response = await PutItemAsync(client, productId, latitude, longitude, quantity);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Cart PUT failed with {(int)response.StatusCode} {response.StatusCode}: {error}");
        }
        var body = await response.Content.ReadFromJsonAsync<CartViewDto>(AuthHelper.Json);
        Assert.NotNull(body);
        return body;
    }

    public static async Task<HttpResponseMessage> CreateOrderAsync(
        HttpClient client,
        double latitude,
        double longitude,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/consumer/orders")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new CreateOrderRequest(latitude, longitude), AuthHelper.Json),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    public static async Task<ConsumerOrderDto> CreateOrderBodyAsync(
        HttpClient client,
        double latitude,
        double longitude,
        string idempotencyKey)
    {
        var response = await CreateOrderAsync(client, latitude, longitude, idempotencyKey);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConsumerOrderDto>(AuthHelper.Json);
        Assert.NotNull(body);
        return body;
    }
}
