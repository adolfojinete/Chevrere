using System.Net;
using System.Net.Http.Json;
using YaaJuu.Modules.Catalog.Application.Contracts;
using YaaJuu.Modules.Catalog.Domain;

namespace YaaJuu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CatalogAdminTests(YaaJuuApiFactory factory)
{
    [Fact]
    public async Task Admin_can_create_list_and_get_category_and_product()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CreateCategoryAsync(admin, "BEB-ADM", "Bebidas Admin");
        var product = await CreateProductAsync(admin, category.Id, "COCACOLA-ADM", "Coca-Cola Original 1.5L", "7701111111111");

        Assert.Equal(GlobalProductStatus.Active, product.Status);
        Assert.Equal(category.Id, product.CategoryId);

        var fetched = await admin.GetFromJsonAsync<GlobalProductDto>($"/api/v1/admin/products/{product.Id}", AuthHelper.Json);
        Assert.NotNull(fetched);
        Assert.Equal("COCACOLA-ADM", fetched.Sku);

        var list = await admin.GetFromJsonAsync<PagedResultDto<GlobalProductDto>>(
            "/api/v1/admin/products?search=coca&page=1&pageSize=20",
            AuthHelper.Json);
        Assert.NotNull(list);
        Assert.Contains(list.Items, p => p.Id == product.Id);

        var categories = await admin.GetFromJsonAsync<PagedResultDto<CategoryDto>>(
            "/api/v1/admin/categories?search=Bebidas",
            AuthHelper.Json);
        Assert.NotNull(categories);
        Assert.Contains(categories.Items, c => c.Id == category.Id);
    }

    [Fact]
    public async Task Duplicate_sku_and_barcode_are_rejected()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CreateCategoryAsync(admin, "BEB-DUP", "Bebidas Dup");
        var first = await CreateProductAsync(admin, category.Id, "SKU-DUP-1", "Producto A", "7702222222222");

        var skuConflict = await admin.PostAsJsonAsync(
            "/api/v1/admin/products",
            new CreateGlobalProductRequest(category.Id, "SKU-DUP-1", "Otro", "Marca", "330 ml", null, null));
        Assert.Equal(HttpStatusCode.Conflict, skuConflict.StatusCode);

        var barcodeConflict = await admin.PostAsJsonAsync(
            "/api/v1/admin/products",
            new CreateGlobalProductRequest(category.Id, "SKU-DUP-2", "Otro", "Marca", "330 ml", null, first.Barcode));
        Assert.Equal(HttpStatusCode.Conflict, barcodeConflict.StatusCode);
    }

    [Fact]
    public async Task Owner_cannot_create_admin_product_and_anonymous_is_unauthorized()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/franchisees",
            AuthHelper.FranchiseeRequest("CATOWN", "owner-catown@example.com", "919111111"));
        created.EnsureSuccessStatusCode();

        using var owner = factory.CreateClientUnredirected();
        var token = await AuthHelper.LoginAsync(owner, "owner-catown@example.com", "OwnerTest!23456");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var forbidden = await owner.PostAsJsonAsync(
            "/api/v1/admin/products",
            new CreateGlobalProductRequest(Guid.CreateVersion7(), "SKU-X", "X", "Y", "1 L", null, null));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var anonymous = factory.CreateClientUnredirected();
        var unauthorized = await anonymous.GetAsync("/api/v1/admin/products");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    internal static async Task<CategoryDto> CreateCategoryAsync(HttpClient admin, string code, string name)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/categories",
            new CreateCategoryRequest(code, name, null, 0));
        response.EnsureSuccessStatusCode();
        var category = await response.Content.ReadFromJsonAsync<CategoryDto>(AuthHelper.Json);
        Assert.NotNull(category);
        return category;
    }

    internal static async Task<GlobalProductDto> CreateProductAsync(
        HttpClient admin,
        Guid categoryId,
        string sku,
        string name,
        string? barcode)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/products",
            new CreateGlobalProductRequest(categoryId, sku, name, "Marca", "1.5 L", null, barcode));
        response.EnsureSuccessStatusCode();
        var product = await response.Content.ReadFromJsonAsync<GlobalProductDto>(AuthHelper.Json);
        Assert.NotNull(product);
        return product;
    }
}
