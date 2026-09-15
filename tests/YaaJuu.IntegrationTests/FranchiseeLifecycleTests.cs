using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using YaaJuu.Modules.Subscriptions.Domain;
using YaaJuu.Modules.Tenancy.Application.Contracts;
using YaaJuu.Modules.Tenancy.Domain;

namespace YaaJuu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class FranchiseeLifecycleTests(YaaJuuApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = AuthHelper.Json;

    [Fact]
    public async Task Activate_suspend_and_reactivate_preserve_data()
    {
        using var client = await AuthHelper.AuthenticatedAdminAsync(factory);
        var created = await client.PostAsJsonAsync(
            "/api/v1/admin/franchisees",
            AuthHelper.FranchiseeRequest("LIFE1", "owner-life1@example.com", "900444444"));
        created.EnsureSuccessStatusCode();
        var ids = await created.Content.ReadFromJsonAsync<CreateFranchiseeResponse>(Json);
        Assert.NotNull(ids);

        var activate = await client.PostAsync($"/api/v1/admin/franchisees/{ids.FranchiseeId}/activate", null);
        Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);

        var suspend = await client.PostAsJsonAsync(
            $"/api/v1/admin/franchisees/{ids.FranchiseeId}/suspend",
            new SuspendFranchiseeRequest("Missed SaaS payment"));
        Assert.Equal(HttpStatusCode.NoContent, suspend.StatusCode);

        var suspended = await client.GetFromJsonAsync<FranchiseeDetailDto>(
            $"/api/v1/admin/franchisees/{ids.FranchiseeId}",
            Json);
        Assert.NotNull(suspended);
        Assert.Equal(FranchiseeStatus.Suspended, suspended.Status);
        Assert.Equal(TenantStatus.Suspended, suspended.TenantStatus);
        Assert.Equal(SubscriptionStatus.Suspended, suspended.Subscription?.Status);
        Assert.Equal(ids.StoreId, suspended.Stores[0].Id);
        Assert.Equal(ids.OwnerUserId, suspended.Owner?.UserId);

        var reactivate = await client.PostAsync($"/api/v1/admin/franchisees/{ids.FranchiseeId}/reactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);

        var reactivated = await client.GetFromJsonAsync<FranchiseeDetailDto>(
            $"/api/v1/admin/franchisees/{ids.FranchiseeId}",
            Json);
        Assert.NotNull(reactivated);
        Assert.Equal(FranchiseeStatus.Active, reactivated.Status);
        Assert.Equal(TenantStatus.Active, reactivated.TenantStatus);
        Assert.Equal(SubscriptionStatus.Active, reactivated.Subscription?.Status);
        Assert.Equal(ids.StoreId, reactivated.Stores[0].Id);
        Assert.Equal(ids.OwnerUserId, reactivated.Owner?.UserId);

        var audit = await client.GetFromJsonAsync<IReadOnlyList<AuditEventDto>>(
            $"/api/v1/admin/audit-events?franchiseeId={ids.FranchiseeId}",
            Json);
        Assert.NotNull(audit);
        Assert.Contains(audit, e => e.Action.Contains("created", StringComparison.OrdinalIgnoreCase) || e.Action.Contains("franchisee", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(audit, e => e.Action == "franchisee.suspended");
        Assert.Contains(audit, e => e.Action == "franchisee.reactivated");
    }

    [Fact]
    public async Task Invalid_transition_is_rejected()
    {
        using var client = await AuthHelper.AuthenticatedAdminAsync(factory);
        var created = await client.PostAsJsonAsync(
            "/api/v1/admin/franchisees",
            AuthHelper.FranchiseeRequest("LIFE2", "owner-life2@example.com", "900555555"));
        created.EnsureSuccessStatusCode();
        var ids = await created.Content.ReadFromJsonAsync<CreateFranchiseeResponse>(Json);
        Assert.NotNull(ids);

        var suspend = await client.PostAsJsonAsync(
            $"/api/v1/admin/franchisees/{ids.FranchiseeId}/suspend",
            new SuspendFranchiseeRequest("Too early"));
        Assert.Equal(HttpStatusCode.Conflict, suspend.StatusCode);
    }
}
