using System.Net.Http.Json;
using System.Reflection;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Catalog.Application.Contracts;
using Chevrere.Modules.Catalog.Domain;
using Chevrere.Modules.Tenancy.Application.Contracts;
using Chevrere.SharedKernel.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CatalogIntegrityTests(ChevrereApiFactory factory)
{
    [Fact]
    public async Task Database_rejects_store_product_when_store_belongs_to_another_tenant()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-FK", "Bebidas FK");
        var product = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "COCACOLA-FK", "Coca-Cola FK", "7705555555555");

        var a = await CreateFranchiseeAsync(admin, "FKCA", "owner-fkca@example.com", "919777777");
        var b = await CreateFranchiseeAsync(admin, "FKCB", "owner-fkcb@example.com", "919888888");
        Assert.NotEqual(a.TenantId, b.TenantId);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();

        var rogue = BypassDomainStoreProduct(a.TenantId, b.StoreId, product.Id);
        db.StoreProducts.Add(rogue);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.NotNull(ex.InnerException);
        Assert.Contains("violates foreign key constraint", ex.InnerException.Message, StringComparison.OrdinalIgnoreCase);
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

    private static StoreProduct BypassDomainStoreProduct(Guid tenantId, Guid storeId, Guid productId)
    {
        var offering = (StoreProduct)Activator.CreateInstance(typeof(StoreProduct), nonPublic: true)!;
        Set(offering, nameof(StoreProduct.Id), Guid.CreateVersion7());
        Set(offering, nameof(StoreProduct.TenantId), tenantId);
        Set(offering, nameof(StoreProduct.StoreId), storeId);
        Set(offering, nameof(StoreProduct.GlobalProductId), productId);
        Set(offering, nameof(StoreProduct.IsEnabled), true);
        Set(offering, nameof(StoreProduct.CreatedAt), DateTimeOffset.UtcNow);
        Set(offering, nameof(StoreProduct.UpdatedAt), DateTimeOffset.UtcNow);
        return offering;
    }

    private static void Set(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(target, value);
    }
}
