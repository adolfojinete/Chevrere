using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using YaaJuu.Modules.Tenancy.Application.Contracts;

namespace YaaJuu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TenantIsolationTests(YaaJuuApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = AuthHelper.Json;

    [Fact]
    public async Task Owner_of_tenant_a_cannot_see_store_of_tenant_b()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);

        var createdA = await admin.PostAsJsonAsync(
            "/api/v1/admin/franchisees",
            AuthHelper.FranchiseeRequest("ISOA", "owner-iso-a@example.com", "900666666"));
        createdA.EnsureSuccessStatusCode();
        var a = await createdA.Content.ReadFromJsonAsync<CreateFranchiseeResponse>(Json);
        Assert.NotNull(a);

        var createdB = await admin.PostAsJsonAsync(
            "/api/v1/admin/franchisees",
            AuthHelper.FranchiseeRequest("ISOB", "owner-iso-b@example.com", "900777777"));
        createdB.EnsureSuccessStatusCode();
        var b = await createdB.Content.ReadFromJsonAsync<CreateFranchiseeResponse>(Json);
        Assert.NotNull(b);

        using var ownerA = factory.CreateClientUnredirected();
        var tokenA = await AuthHelper.LoginAsync(ownerA, "owner-iso-a@example.com", "OwnerTest!23456");
        ownerA.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        var ownStores = await ownerA.GetFromJsonAsync<IReadOnlyList<StoreDto>>("/api/v1/business/stores", Json);
        Assert.NotNull(ownStores);
        Assert.Single(ownStores);
        Assert.Equal(a.StoreId, ownStores[0].Id);
        Assert.DoesNotContain(ownStores, s => s.Id == b.StoreId);

        var foreign = await ownerA.GetAsync($"/api/v1/business/stores/{b.StoreId}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var adminOnly = await ownerA.GetAsync($"/api/v1/admin/franchisees/{b.FranchiseeId}");
        Assert.Equal(HttpStatusCode.Forbidden, adminOnly.StatusCode);
    }

    [Fact]
    public async Task Anonymous_request_is_unauthorized()
    {
        using var client = factory.CreateClientUnredirected();
        var response = await client.GetAsync("/api/v1/admin/franchisees");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
