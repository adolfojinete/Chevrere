using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Tenancy.Application.Contracts;
using YaaJuu.Modules.Tenancy.Domain;
using YaaJuu.Modules.Tenancy.Domain.ValueObjects;
using YaaJuu.SharedKernel.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class StoreTenantIntegrityTests(YaaJuuApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = AuthHelper.Json;

    [Fact]
    public async Task Database_accepts_store_when_tenant_matches_franchisee()
    {
        var pair = await SeedTwoFranchiseesAsync("OKA", "OKB", "918111111", "918222222");

        await using var scope = factory.Services.CreateAsyncScope();
        EnableBypass(scope);
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();

        var tenantA = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == pair.A.TenantId);
        var franchiseeA = await db.Franchisees.AsNoTracking().SingleAsync(f => f.Id == pair.A.FranchiseeId);

        var store = Store.Create(
            tenantA,
            franchiseeA,
            TenantCode.Create("DS-OKA-2"),
            "Dark Store extra",
            "Calle 80 # 1-20",
            null,
            DateTimeOffset.UtcNow);

        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var persisted = await db.Stores.AsNoTracking().SingleAsync(s => s.Id == store.Id);
        Assert.Equal(franchiseeA.TenantId, persisted.TenantId);
        Assert.Equal(franchiseeA.Id, persisted.FranchiseeId);
    }

    [Fact]
    public async Task Database_rejects_store_when_franchisee_belongs_to_another_tenant()
    {
        var pair = await SeedTwoFranchiseesAsync("MSA", "MSB", "918333333", "918444444");

        await using var scope = factory.Services.CreateAsyncScope();
        EnableBypass(scope);
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();

        var rogue = BypassDomainStore(
            tenantId: pair.A.TenantId,
            franchiseeId: pair.B.FranchiseeId,
            code: "DS-CROSS-TENANT");

        db.Stores.Add(rogue);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.NotNull(ex.InnerException);
        Assert.Contains("violates foreign key constraint", ex.InnerException.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(CreateFranchiseeResponse A, CreateFranchiseeResponse B)> SeedTwoFranchiseesAsync(
        string suffixA,
        string suffixB,
        string identificationA,
        string identificationB)
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);

        var createdA = await admin.PostAsJsonAsync(
            "/api/v1/admin/franchisees",
            AuthHelper.FranchiseeRequest(suffixA, $"owner-{suffixA}@example.com", identificationA));
        createdA.EnsureSuccessStatusCode();
        var a = await createdA.Content.ReadFromJsonAsync<CreateFranchiseeResponse>(Json);
        Assert.NotNull(a);

        var createdB = await admin.PostAsJsonAsync(
            "/api/v1/admin/franchisees",
            AuthHelper.FranchiseeRequest(suffixB, $"owner-{suffixB}@example.com", identificationB));
        createdB.EnsureSuccessStatusCode();
        var b = await createdB.Content.ReadFromJsonAsync<CreateFranchiseeResponse>(Json);
        Assert.NotNull(b);

        Assert.NotEqual(a.TenantId, b.TenantId);
        return (a, b);
    }

    private static void EnableBypass(IServiceScope scope)
    {
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
    }

    private static Store BypassDomainStore(Guid tenantId, Guid franchiseeId, string code)
    {
        var store = (Store)Activator.CreateInstance(typeof(Store), nonPublic: true)!;
        Set(store, nameof(Store.Id), Guid.CreateVersion7());
        Set(store, nameof(Store.TenantId), tenantId);
        Set(store, nameof(Store.FranchiseeId), franchiseeId);
        Set(store, nameof(Store.Code), code);
        Set(store, nameof(Store.Name), "Cross-tenant store");
        Set(store, nameof(Store.AddressInternal), "Should never persist");
        Set(store, nameof(Store.Status), StoreStatus.Pending);
        Set(store, nameof(Store.CreatedAt), DateTimeOffset.UtcNow);
        Set(store, nameof(Store.UpdatedAt), DateTimeOffset.UtcNow);
        return store;
    }

    private static void Set(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(target, value);
    }
}
