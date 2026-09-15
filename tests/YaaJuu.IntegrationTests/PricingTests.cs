using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using YaaJuu.Infrastructure.Audit;
using YaaJuu.Infrastructure.Identity;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Pricing.Application.Contracts;
using YaaJuu.Modules.Pricing.Domain;
using YaaJuu.Modules.Tenancy.Application.Contracts;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PricingTests(YaaJuuApiFactory factory)
{
    [Fact]
    public async Task Global_and_store_pricing_flow_with_idempotency_and_fallback()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-PRC", "Bebidas Pricing");
        var product = await CatalogAdminTests.CreateProductAsync(
            admin,
            category.Id,
            "COCACOLA-PRC",
            "Coca-Cola Pricing 1.5L",
            "7709999999901");
        var franchisee = await CreateFranchiseeAsync(admin, "PRC1", "owner-prc1@example.com", "918999001");

        var set1 = await admin.PutAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/price",
            new SetPriceRequest(6000m, "COP"));
        set1.EnsureSuccessStatusCode();
        var global1 = await set1.Content.ReadFromJsonAsync<GlobalPriceDto>(AuthHelper.Json);
        Assert.NotNull(global1);
        Assert.Equal(6000m, global1.SuggestedPrice!.Amount);

        var setSame = await admin.PutAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/price",
            new SetPriceRequest(6000m, "COP"));
        setSame.EnsureSuccessStatusCode();
        Assert.Equal(1, await CountAuditsAsync(
            a => a.Action == AuditActions.GlobalSuggestedPriceSet || a.Action == AuditActions.GlobalSuggestedPriceChanged,
            product.Id));
        Assert.Equal(1, await CountCurrentGlobalPricesAsync(product.Id));

        var set2 = await admin.PutAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/price",
            new SetPriceRequest(6500m, "COP"));
        set2.EnsureSuccessStatusCode();
        Assert.Equal(2, await CountGlobalPriceHistoryAsync(product.Id));
        Assert.Equal(1, await CountCurrentGlobalPricesAsync(product.Id));

        using var owner = await OwnerClientAsync("owner-prc1@example.com");
        var enable = await owner.PostAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/enable",
            null);
        enable.EnsureSuccessStatusCode();

        var withoutOverride = await owner.GetFromJsonAsync<StoreEffectivePriceDto>(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/price",
            AuthHelper.Json);
        Assert.NotNull(withoutOverride);
        Assert.Equal(6500m, withoutOverride.EffectivePrice!.Amount);
        Assert.Equal(PriceSource.Global, withoutOverride.Source);
        Assert.Null(withoutOverride.OverridePrice);

        var putOverride = await owner.PutAsJsonAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/price",
            new SetPriceRequest(6800m, "COP"));
        putOverride.EnsureSuccessStatusCode();
        var withOverride = await putOverride.Content.ReadFromJsonAsync<StoreEffectivePriceDto>(AuthHelper.Json);
        Assert.NotNull(withOverride);
        Assert.Equal(6800m, withOverride.EffectivePrice!.Amount);
        Assert.Equal(PriceSource.Store, withOverride.Source);

        var putSame = await owner.PutAsJsonAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/price",
            new SetPriceRequest(6800m, "COP"));
        putSame.EnsureSuccessStatusCode();
        Assert.Equal(1, await CountAuditsForStoreProductAsync(franchisee.StoreId, product.Id));

        var remove = await owner.DeleteAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/price");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        var afterRemove = await owner.GetFromJsonAsync<StoreEffectivePriceDto>(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/price",
            AuthHelper.Json);
        Assert.NotNull(afterRemove);
        Assert.Equal(6500m, afterRemove.EffectivePrice!.Amount);
        Assert.Equal(PriceSource.Global, afterRemove.Source);

        var removeAgain = await owner.DeleteAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/price");
        Assert.Equal(HttpStatusCode.NoContent, removeAgain.StatusCode);
        Assert.Equal(1, await CountStorePriceRemovedAuditsAsync(franchisee.StoreId, product.Id));
    }

    [Fact]
    public async Task Store_only_override_works_without_global_suggested_price()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-SOP", "Bebidas StoreOnly");
        var product = await CatalogAdminTests.CreateProductAsync(
            admin,
            category.Id,
            "SKU-SOP-1",
            "Producto StoreOnly",
            null);
        var franchisee = await CreateFranchiseeAsync(admin, "SOP1", "owner-sop1@example.com", "918999002");
        using var owner = await OwnerClientAsync("owner-sop1@example.com");
        (await owner.PostAsync($"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/enable", null))
            .EnsureSuccessStatusCode();

        var none = await owner.GetFromJsonAsync<StoreEffectivePriceDto>(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/price",
            AuthHelper.Json);
        Assert.NotNull(none);
        Assert.Equal(PriceSource.None, none.Source);
        Assert.Null(none.EffectivePrice);

        var put = await owner.PutAsJsonAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/price",
            new SetPriceRequest(6200m, "COP"));
        put.EnsureSuccessStatusCode();
        var priced = await put.Content.ReadFromJsonAsync<StoreEffectivePriceDto>(AuthHelper.Json);
        Assert.NotNull(priced);
        Assert.Equal(6200m, priced.EffectivePrice!.Amount);
        Assert.Equal(PriceSource.Store, priced.Source);
        Assert.Null(priced.SuggestedPrice);

        var remove = await owner.DeleteAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/price");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        var afterRemove = await owner.GetFromJsonAsync<StoreEffectivePriceDto>(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/price",
            AuthHelper.Json);
        Assert.NotNull(afterRemove);
        Assert.Null(afterRemove.EffectivePrice);
        Assert.Equal(PriceSource.None, afterRemove.Source);
        Assert.Equal(1, await CountStorePriceRemovedAuditsAsync(franchisee.StoreId, product.Id));
    }

    [Fact]
    public async Task Remove_override_requires_valid_product_association_and_allows_noop()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-RMOV", "Bebidas Remove");
        var offered = await CatalogAdminTests.CreateProductAsync(
            admin,
            category.Id,
            "SKU-RMOV-1",
            "Producto Remove Offered",
            null);
        var notOffered = await CatalogAdminTests.CreateProductAsync(
            admin,
            category.Id,
            "SKU-RMOV-2",
            "Producto Remove Not Offered",
            null);
        var franchisee = await CreateFranchiseeAsync(admin, "RMOV1", "owner-rmov1@example.com", "918991201");
        using var owner = await OwnerClientAsync("owner-rmov1@example.com");

        (await owner.PostAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{offered.Id}/enable",
            null)).EnsureSuccessStatusCode();

        var missingProduct = await owner.DeleteAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{Guid.CreateVersion7()}/price");
        Assert.Equal(HttpStatusCode.NotFound, missingProduct.StatusCode);

        var notAssociated = await owner.DeleteAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{notOffered.Id}/price");
        Assert.Equal(HttpStatusCode.Conflict, notAssociated.StatusCode);
        var problem = await notAssociated.Content.ReadFromJsonAsync<ProblemDetails>(AuthHelper.Json);
        Assert.NotNull(problem);
        Assert.Equal("store_price.product.not_offered", problem.Title);

        var noop = await owner.DeleteAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{offered.Id}/price");
        Assert.Equal(HttpStatusCode.NoContent, noop.StatusCode);
        Assert.Equal(0, await CountStorePriceRemovedAuditsAsync(franchisee.StoreId, offered.Id));
        Assert.Equal(0, await CountStorePriceHistoryAsync(franchisee.StoreId, offered.Id));
    }

    [Fact]
    public async Task Owner_cannot_price_foreign_store_and_support_cannot_write_global()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-PISO", "Bebidas Price Iso");
        var product = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-PISO-1", "Price Iso", null);
        var a = await CreateFranchiseeAsync(admin, "PISOA", "owner-pisoa@example.com", "918991103");
        var b = await CreateFranchiseeAsync(admin, "PISOB", "owner-pisob@example.com", "918991104");

        using var ownerA = await OwnerClientAsync("owner-pisoa@example.com");
        (await ownerA.PostAsync($"/api/v1/business/stores/{a.StoreId}/products/{product.Id}/enable", null))
            .EnsureSuccessStatusCode();

        var foreign = await ownerA.PutAsJsonAsync(
            $"/api/v1/business/stores/{b.StoreId}/products/{product.Id}/price",
            new SetPriceRequest(1000m, "COP"));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var foreignDelete = await ownerA.DeleteAsync(
            $"/api/v1/business/stores/{b.StoreId}/products/{product.Id}/price");
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);

        await EnsureSupportUserAsync();
        using var support = factory.CreateClientUnredirected();
        var token = await AuthHelper.LoginAsync(support, "support-pricing@yaajuu.test", "SupportTest!23456");
        support.DefaultRequestHeaders.Authorization = new("Bearer", token);

        (await admin.PutAsJsonAsync($"/api/v1/admin/products/{product.Id}/price", new SetPriceRequest(5000m, "COP")))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync($"/api/v1/admin/products/{product.Id}/price")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await support.PutAsJsonAsync($"/api/v1/admin/products/{product.Id}/price", new SetPriceRequest(5100m, "COP")))
                .StatusCode);
    }

    [Fact]
    public async Task Database_rejects_store_price_for_foreign_tenant_or_missing_store_product()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-DBP", "Bebidas DbPrice");
        var product = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-DBP-1", "DbPrice", null);
        var a = await CreateFranchiseeAsync(admin, "DBPA", "owner-dbpa@example.com", "918999005");
        var b = await CreateFranchiseeAsync(admin, "DBPB", "owner-dbpb@example.com", "918999006");

        using var ownerA = await OwnerClientAsync("owner-dbpa@example.com");
        (await ownerA.PostAsync($"/api/v1/business/stores/{a.StoreId}/products/{product.Id}/enable", null))
            .EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();

        var rogueTenant = BypassStorePrice(a.TenantId, b.StoreId, product.Id, 1000m);
        db.StoreProductPrices.Add(rogueTenant);
        var fkEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("foreign key", fkEx.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();

        var withoutOffering = BypassStorePrice(b.TenantId, b.StoreId, product.Id, 1000m);
        db.StoreProductPrices.Add(withoutOffering);
        var offeringEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("foreign key", offeringEx.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureSupportUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        const string email = "support-pricing@yaajuu.test";
        if (await users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Platform Support Pricing",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        Assert.True((await users.CreateAsync(user, "SupportTest!23456")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, RoleNames.PlatformSupport)).Succeeded);
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

    private async Task<int> CountAuditsAsync(Func<AuditEvent, bool> predicate, Guid? productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var events = await db.Set<AuditEvent>().AsNoTracking().ToListAsync();
        if (productId is Guid id)
        {
            var priceIds = await db.GlobalProductPrices.AsNoTracking()
                .Where(p => p.GlobalProductId == id)
                .Select(p => p.Id)
                .ToListAsync();
            return events.Count(e => predicate(e) && priceIds.Contains(e.EntityId));
        }

        return events.Count(predicate);
    }

    private async Task<int> CountAuditsForStoreProductAsync(Guid storeId, Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var priceIds = await db.StoreProductPrices.AsNoTracking()
            .Where(p => p.StoreId == storeId && p.GlobalProductId == productId)
            .Select(p => p.Id)
            .ToListAsync();
        return await db.Set<AuditEvent>().CountAsync(e =>
            (e.Action == AuditActions.StorePriceSet || e.Action == AuditActions.StorePriceChanged)
            && priceIds.Contains(e.EntityId));
    }

    private async Task<int> CountStorePriceHistoryAsync(Guid storeId, Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        return await db.StoreProductPrices.CountAsync(p => p.StoreId == storeId && p.GlobalProductId == productId);
    }

    private async Task<int> CountStorePriceRemovedAuditsAsync(Guid storeId, Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var priceIds = await db.StoreProductPrices.AsNoTracking()
            .Where(p => p.StoreId == storeId && p.GlobalProductId == productId)
            .Select(p => p.Id)
            .ToListAsync();
        return await db.Set<AuditEvent>().CountAsync(e =>
            e.Action == AuditActions.StorePriceRemoved && priceIds.Contains(e.EntityId));
    }

    private async Task<int> CountCurrentGlobalPricesAsync(Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        return await db.GlobalProductPrices.CountAsync(p => p.GlobalProductId == productId && p.ValidTo == null);
    }

    private async Task<int> CountGlobalPriceHistoryAsync(Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        return await db.GlobalProductPrices.CountAsync(p => p.GlobalProductId == productId);
    }

    private static StoreProductPrice BypassStorePrice(Guid tenantId, Guid storeId, Guid productId, decimal amount)
    {
        var price = (StoreProductPrice)Activator.CreateInstance(typeof(StoreProductPrice), nonPublic: true)!;
        Set(price, nameof(StoreProductPrice.Id), Guid.CreateVersion7());
        Set(price, nameof(StoreProductPrice.TenantId), tenantId);
        Set(price, nameof(StoreProductPrice.StoreId), storeId);
        Set(price, nameof(StoreProductPrice.GlobalProductId), productId);
        Set(price, nameof(StoreProductPrice.Amount), amount);
        Set(price, nameof(StoreProductPrice.Currency), "COP");
        Set(price, nameof(StoreProductPrice.ValidFrom), DateTimeOffset.UtcNow);
        Set(price, nameof(StoreProductPrice.CreatedAt), DateTimeOffset.UtcNow);
        Set(price, nameof(StoreProductPrice.UpdatedAt), DateTimeOffset.UtcNow);
        return price;
    }

    private static void Set(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(target, value);
    }
}
