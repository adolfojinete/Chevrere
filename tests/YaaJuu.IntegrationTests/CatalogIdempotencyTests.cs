using System.Net;
using System.Net.Http.Json;
using YaaJuu.Infrastructure.Audit;
using YaaJuu.Infrastructure.Identity;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Catalog.Application.Contracts;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Tenancy.Application.Contracts;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CatalogIdempotencyTests(YaaJuuApiFactory factory)
{
    [Fact]
    public async Task Enable_and_disable_store_product_are_idempotent_for_audit_and_updated_at()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-IDP", "Bebidas Idempotent");
        var product = await CatalogAdminTests.CreateProductAsync(
            admin,
            category.Id,
            "COCACOLA-IDP",
            "Coca-Cola Idempotent",
            "7706666666666");
        var franchisee = await CreateFranchiseeAsync(admin, "IDP1", "owner-idp1@example.com", "918666666");

        using var owner = await OwnerClientAsync("owner-idp1@example.com");

        var firstEnable = await owner.PostAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/enable",
            null);
        firstEnable.EnsureSuccessStatusCode();
        var enabled = await firstEnable.Content.ReadFromJsonAsync<StoreProductDto>(AuthHelper.Json);
        Assert.NotNull(enabled);
        Assert.True(enabled.IsEnabled);
        var offeringId = enabled.Id;
        // Persistido en PostgreSQL (precisión µs); no comparar con el valor in-memory del primer response.
        var updatedAfterEnable = await GetStoreProductUpdatedAtAsync(offeringId);

        var secondEnable = await owner.PostAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/enable",
            null);
        secondEnable.EnsureSuccessStatusCode();
        var enabledAgain = await secondEnable.Content.ReadFromJsonAsync<StoreProductDto>(AuthHelper.Json);
        Assert.NotNull(enabledAgain);
        Assert.Equal(offeringId, enabledAgain.Id);
        Assert.True(enabledAgain.IsEnabled);
        Assert.Equal(updatedAfterEnable, await GetStoreProductUpdatedAtAsync(offeringId));

        Assert.Equal(1, await CountAuditsAsync(AuditActions.StoreProductEnabled, offeringId));
        Assert.Equal(1, await CountStoreProductsAsync(franchisee.StoreId, product.Id));

        var firstDisable = await owner.PostAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/disable",
            null);
        Assert.Equal(HttpStatusCode.NoContent, firstDisable.StatusCode);

        var updatedAfterDisable = await GetStoreProductUpdatedAtAsync(offeringId);
        Assert.NotEqual(updatedAfterEnable, updatedAfterDisable);
        var afterDisable = await GetStoreProductAsync(owner, franchisee.StoreId, product.Id);
        Assert.False(afterDisable.IsEnabled);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.StoreProductDisabled, offeringId));

        var secondDisable = await owner.PostAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/disable",
            null);
        Assert.Equal(HttpStatusCode.NoContent, secondDisable.StatusCode);

        var afterSecondDisable = await GetStoreProductAsync(owner, franchisee.StoreId, product.Id);
        Assert.False(afterSecondDisable.IsEnabled);
        Assert.Equal(updatedAfterDisable, await GetStoreProductUpdatedAtAsync(offeringId));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.StoreProductEnabled, offeringId));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.StoreProductDisabled, offeringId));
        Assert.Equal(1, await CountStoreProductsAsync(franchisee.StoreId, product.Id));
    }

    [Fact]
    public async Task Category_and_product_activate_deactivate_are_idempotent()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-STT", "Bebidas Status");
        var product = await CatalogAdminTests.CreateProductAsync(
            admin,
            category.Id,
            "SKU-STT-1",
            "Producto Status",
            null);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/admin/categories/{category.Id}/activate", null)).StatusCode);
        Assert.Equal(0, await CountAuditsAsync(AuditActions.CategoryActivated, category.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/admin/categories/{category.Id}/deactivate", null)).StatusCode);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.CategoryDeactivated, category.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/admin/categories/{category.Id}/deactivate", null)).StatusCode);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.CategoryDeactivated, category.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/admin/categories/{category.Id}/activate", null)).StatusCode);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.CategoryActivated, category.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/admin/products/{product.Id}/activate", null)).StatusCode);
        Assert.Equal(0, await CountAuditsAsync(AuditActions.GlobalProductActivated, product.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/admin/products/{product.Id}/deactivate", null)).StatusCode);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.GlobalProductDeactivated, product.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/admin/products/{product.Id}/deactivate", null)).StatusCode);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.GlobalProductDeactivated, product.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/admin/products/{product.Id}/activate", null)).StatusCode);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.GlobalProductActivated, product.Id));
    }

    [Fact]
    public async Task Concurrent_enable_requests_produce_single_store_product_and_single_audit()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-CON", "Bebidas Concurrent");
        var product = await CatalogAdminTests.CreateProductAsync(
            admin,
            category.Id,
            "SKU-CON-1",
            "Producto Concurrent",
            "7708888888881");
        var franchisee = await CreateFranchiseeAsync(admin, "CON1", "owner-con1@example.com", "918888001");

        using var ownerA = await OwnerClientAsync("owner-con1@example.com");
        using var ownerB = await OwnerClientAsync("owner-con1@example.com");
        var url = $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/enable";

        var firstTask = ownerA.PostAsync(url, null);
        var secondTask = ownerB.PostAsync(url, null);
        var responses = await Task.WhenAll(firstTask, secondTask);
        var first = responses[0];
        var second = responses[1];

        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());

        var dtoA = await first.Content.ReadFromJsonAsync<StoreProductDto>(AuthHelper.Json);
        var dtoB = await second.Content.ReadFromJsonAsync<StoreProductDto>(AuthHelper.Json);
        Assert.NotNull(dtoA);
        Assert.NotNull(dtoB);
        Assert.Equal(dtoA.Id, dtoB.Id);
        Assert.True(dtoA.IsEnabled);
        Assert.True(dtoB.IsEnabled);

        Assert.Equal(1, await CountStoreProductsAsync(franchisee.StoreId, product.Id));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.StoreProductEnabled, dtoA.Id));
    }

    [Fact]
    public async Task Platform_support_can_read_catalog_but_cannot_write()
    {
        await EnsureSupportUserAsync();
        using var support = factory.CreateClientUnredirected();
        var token = await AuthHelper.LoginAsync(support, "support@yaajuu.test", "SupportTest!23456");
        support.DefaultRequestHeaders.Authorization = new("Bearer", token);

        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync("/api/v1/admin/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync("/api/v1/admin/products")).StatusCode);

        var createCategory = await support.PostAsJsonAsync(
            "/api/v1/admin/categories",
            new CreateCategoryRequest("BEB-SUP", "No Write", null, 0));
        Assert.Equal(HttpStatusCode.Forbidden, createCategory.StatusCode);

        var createProduct = await support.PostAsJsonAsync(
            "/api/v1/admin/products",
            new CreateGlobalProductRequest(Guid.CreateVersion7(), "SKU-SUP", "X", "Y", "1 L", null, null));
        Assert.Equal(HttpStatusCode.Forbidden, createProduct.StatusCode);
    }

    private async Task EnsureSupportUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = await users.FindByEmailAsync("support@yaajuu.test");
        if (existing is not null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = "support@yaajuu.test",
            Email = "support@yaajuu.test",
            EmailConfirmed = true,
            DisplayName = "Platform Support",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        var create = await users.CreateAsync(user, "SupportTest!23456");
        Assert.True(create.Succeeded, string.Join(", ", create.Errors.Select(e => e.Description)));
        var role = await users.AddToRoleAsync(user, RoleNames.PlatformSupport);
        Assert.True(role.Succeeded, string.Join(", ", role.Errors.Select(e => e.Description)));
    }

    private async Task<HttpClient> OwnerClientAsync(string email)
    {
        var client = factory.CreateClientUnredirected();
        var token = await AuthHelper.LoginAsync(client, email, "OwnerTest!23456");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task<CreateFranchiseeResponse> CreateFranchiseeAsync(
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

    private static async Task<StoreProductDto> GetStoreProductAsync(HttpClient owner, Guid storeId, Guid productId)
    {
        var list = await owner.GetFromJsonAsync<IReadOnlyList<StoreProductDto>>(
            $"/api/v1/business/stores/{storeId}/products",
            AuthHelper.Json);
        Assert.NotNull(list);
        return Assert.Single(list, p => p.GlobalProductId == productId);
    }

    private async Task<int> CountAuditsAsync(string action, Guid entityId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        return await db.Set<AuditEvent>().CountAsync(e => e.Action == action && e.EntityId == entityId);
    }

    private async Task<int> CountStoreProductsAsync(Guid storeId, Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        return await db.StoreProducts.CountAsync(s => s.StoreId == storeId && s.GlobalProductId == productId);
    }

    private async Task<DateTimeOffset> GetStoreProductUpdatedAtAsync(Guid storeProductId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        return await db.StoreProducts
            .Where(s => s.Id == storeProductId)
            .Select(s => s.UpdatedAt)
            .SingleAsync();
    }
}
