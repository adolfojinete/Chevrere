using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Chevrere.Modules.Consumer.Application.Contracts;
using Chevrere.Modules.Inventory.Application.Contracts;
using Chevrere.Modules.Pricing.Application.Contracts;
using Chevrere.Modules.Tenancy.Application.Contracts;

namespace Chevrere.IntegrationTests;

/// <summary>
/// Consumer Discovery tests share one database, so every test works on its own patch of the map.
/// Two service areas thousands of kilometres apart can never resolve each other's coordinates.
/// </summary>
internal static class ConsumerTestData
{
    public static async Task<CreateFranchiseeResponse> CreateActiveStoreAsync(
        HttpClient admin,
        string suffix,
        string ownerEmail,
        string identification)
    {
        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/franchisees",
            AuthHelper.FranchiseeRequest(suffix, ownerEmail, identification));
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<CreateFranchiseeResponse>(AuthHelper.Json);
        Assert.NotNull(body);

        // Activating the franchisee is what turns Tenant and Store into Active, which is what
        // Consumer Discovery requires before the store can ever be resolved.
        (await admin.PostAsync($"/api/v1/admin/franchisees/{body.FranchiseeId}/activate", null))
            .EnsureSuccessStatusCode();
        return body;
    }

    public static async Task<HttpClient> OwnerClientAsync(ChevrereApiFactory factory, string email)
    {
        var client = factory.CreateClientUnredirected();
        var token = await AuthHelper.LoginAsync(client, email, "OwnerTest!23456");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    public static async Task<AdminServiceAreaDto> ConfigureAreaAsync(
        HttpClient admin,
        Guid storeId,
        double latitude,
        double longitude,
        int radiusMeters)
    {
        var response = await admin.PutAsJsonAsync(
            $"/api/v1/admin/stores/{storeId}/service-area",
            new ConfigureServiceAreaRequest(latitude, longitude, radiusMeters));
        response.EnsureSuccessStatusCode();
        var area = await response.Content.ReadFromJsonAsync<AdminServiceAreaDto>(AuthHelper.Json);
        Assert.NotNull(area);
        return area;
    }

    public static async Task EnableAreaAsync(HttpClient admin, Guid storeId) =>
        (await admin.PostAsync($"/api/v1/admin/stores/{storeId}/service-area/enable", null))
            .EnsureSuccessStatusCode();

    public static async Task<AdminServiceAreaDto> OpenAreaAsync(
        HttpClient admin,
        Guid storeId,
        double latitude,
        double longitude,
        int radiusMeters)
    {
        var area = await ConfigureAreaAsync(admin, storeId, latitude, longitude, radiusMeters);
        await EnableAreaAsync(admin, storeId);
        return area;
    }

    public static async Task OfferProductAsync(HttpClient owner, Guid storeId, Guid productId) =>
        (await owner.PostAsync($"/api/v1/business/stores/{storeId}/products/{productId}/enable", null))
            .EnsureSuccessStatusCode();

    public static async Task SetGlobalPriceAsync(HttpClient admin, Guid productId, decimal amount) =>
        (await admin.PutAsJsonAsync(
            $"/api/v1/admin/products/{productId}/price",
            new SetPriceRequest(amount, "COP"))).EnsureSuccessStatusCode();

    public static async Task SetStorePriceAsync(HttpClient owner, Guid storeId, Guid productId, decimal amount) =>
        (await owner.PutAsJsonAsync(
            $"/api/v1/business/stores/{storeId}/products/{productId}/price",
            new SetPriceRequest(amount, "COP"))).EnsureSuccessStatusCode();

    public static async Task InitializeStockAsync(
        HttpClient owner,
        Guid storeId,
        Guid productId,
        long quantity,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/business/stores/{storeId}/inventory/{productId}/initialize")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new InitializeInventoryRequest(quantity), AuthHelper.Json),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        (await owner.SendAsync(request)).EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Makes a product fully buyable in a store: offered, priced and in stock.
    /// </summary>
    public static async Task StockAndPriceAsync(
        HttpClient owner,
        Guid storeId,
        Guid productId,
        long quantity,
        string idempotencyKey,
        decimal? overridePrice = null)
    {
        await OfferProductAsync(owner, storeId, productId);
        if (overridePrice is decimal price)
        {
            await SetStorePriceAsync(owner, storeId, productId, price);
        }

        await InitializeStockAsync(owner, storeId, productId, quantity, idempotencyKey);
    }

    public static Task<HttpResponseMessage> CoverageAsync(HttpClient client, double latitude, double longitude) =>
        client.PostAsJsonAsync("/api/v1/consumer/coverage", new ConsumerLocationRequest(latitude, longitude));

    public static async Task<CoverageDto> CoverageBodyAsync(HttpClient client, double latitude, double longitude)
    {
        var response = await CoverageAsync(client, latitude, longitude);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CoverageDto>(AuthHelper.Json);
        Assert.NotNull(body);
        return body;
    }

    public static async Task<ConsumerCatalogResponse> SearchAsync(
        HttpClient client,
        double latitude,
        double longitude,
        string? search = null,
        Guid? categoryId = null,
        int page = 1,
        int pageSize = 20)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/consumer/catalog/search",
            new ConsumerCatalogSearchRequest
            {
                Latitude = latitude,
                Longitude = longitude,
                Page = page,
                PageSize = pageSize,
                Search = search,
                CategoryId = categoryId
            });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConsumerCatalogResponse>(AuthHelper.Json);
        Assert.NotNull(body);
        return body;
    }

    public static async Task<ConsumerCategoriesResponse> CategoriesAsync(
        HttpClient client,
        double latitude,
        double longitude)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/consumer/categories",
            new ConsumerLocationRequest(latitude, longitude));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConsumerCategoriesResponse>(AuthHelper.Json);
        Assert.NotNull(body);
        return body;
    }

    public static Task<HttpResponseMessage> ProductAsync(
        HttpClient client,
        Guid productId,
        double latitude,
        double longitude) =>
        client.PostAsJsonAsync(
            $"/api/v1/consumer/products/{productId}",
            new ConsumerLocationRequest(latitude, longitude));
}
