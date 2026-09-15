using System.Net;
using System.Net.Http.Json;
using YaaJuu.Modules.Catalog.Application.Contracts;
using YaaJuu.Modules.Tenancy.Application.Contracts;

namespace YaaJuu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CatalogBusinessTests(YaaJuuApiFactory factory)
{
    [Fact]
    public async Task Same_global_product_can_be_enabled_on_two_tenants()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-SHR", "Bebidas Shared");
        var product = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "COCACOLA-SHR", "Coca-Cola Shared 1.5L", "7703333333333");

        var a = await CreateOwnerStoreAsync(admin, "CATA", "owner-cata@example.com", "919222222");
        var b = await CreateOwnerStoreAsync(admin, "CATB", "owner-catb@example.com", "919333333");

        using var ownerA = await OwnerClientAsync("owner-cata@example.com");
        using var ownerB = await OwnerClientAsync("owner-catb@example.com");

        var catalog = await ownerA.GetFromJsonAsync<PagedResultDto<CatalogProductDto>>(
            "/api/v1/business/catalog/products?search=Shared",
            AuthHelper.Json);
        Assert.NotNull(catalog);
        Assert.Contains(catalog.Items, p => p.Id == product.Id);

        var enableA = await ownerA.PostAsync($"/api/v1/business/stores/{a.StoreId}/products/{product.Id}/enable", null);
        enableA.EnsureSuccessStatusCode();
        var enableB = await ownerB.PostAsync($"/api/v1/business/stores/{b.StoreId}/products/{product.Id}/enable", null);
        enableB.EnsureSuccessStatusCode();

        var again = await ownerA.PostAsync($"/api/v1/business/stores/{a.StoreId}/products/{product.Id}/enable", null);
        again.EnsureSuccessStatusCode();

        var listA = await ownerA.GetFromJsonAsync<IReadOnlyList<StoreProductDto>>(
            $"/api/v1/business/stores/{a.StoreId}/products",
            AuthHelper.Json);
        Assert.NotNull(listA);
        Assert.Contains(listA, p => p.GlobalProductId == product.Id && p.IsEnabled && p.IsCommerciallyAvailable);
    }

    [Fact]
    public async Task Owner_a_cannot_enable_or_list_store_b()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-ISO", "Bebidas Iso");
        var product = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "COCACOLA-ISO", "Coca-Cola Iso", "7704444444444");
        var a = await CreateOwnerStoreAsync(admin, "ISOCA", "owner-isoca@example.com", "919444444");
        var b = await CreateOwnerStoreAsync(admin, "ISOCB", "owner-isocb@example.com", "919555555");

        using var ownerA = await OwnerClientAsync("owner-isoca@example.com");
        var enableForeign = await ownerA.PostAsync($"/api/v1/business/stores/{b.StoreId}/products/{product.Id}/enable", null);
        Assert.Equal(HttpStatusCode.NotFound, enableForeign.StatusCode);

        var listForeign = await ownerA.GetAsync($"/api/v1/business/stores/{b.StoreId}/products");
        Assert.Equal(HttpStatusCode.NotFound, listForeign.StatusCode);

        var enableOwn = await ownerA.PostAsync($"/api/v1/business/stores/{a.StoreId}/products/{product.Id}/enable", null);
        enableOwn.EnsureSuccessStatusCode();
        _ = a;
    }

    [Fact]
    public async Task Inactive_category_hides_product_from_business_catalog()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-HID", "Bebidas Hidden");
        var product = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "COCACOLA-HID", "Coca-Cola Hidden", null);
        await CreateOwnerStoreAsync(admin, "CATH", "owner-cath@example.com", "919666666");

        var deactivate = await admin.PostAsync($"/api/v1/admin/categories/{category.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        using var owner = await OwnerClientAsync("owner-cath@example.com");
        var catalog = await owner.GetFromJsonAsync<PagedResultDto<CatalogProductDto>>(
            $"/api/v1/business/catalog/products?search=Hidden",
            AuthHelper.Json);
        Assert.NotNull(catalog);
        Assert.DoesNotContain(catalog.Items, p => p.Id == product.Id);
    }

    private async Task<HttpClient> OwnerClientAsync(string email)
    {
        var client = factory.CreateClientUnredirected();
        var token = await AuthHelper.LoginAsync(client, email, "OwnerTest!23456");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task<CreateFranchiseeResponse> CreateOwnerStoreAsync(
        HttpClient admin,
        string suffix,
        string email,
        string identification)
    {
        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/franchisees",
            AuthHelper.FranchiseeRequest(suffix, email, identification));
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<CreateFranchiseeResponse>(AuthHelper.Json);
        Assert.NotNull(body);
        return body;
    }
}
