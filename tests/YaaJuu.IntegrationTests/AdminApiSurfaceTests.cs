using System.Net;
using System.Net.Http.Json;
using YaaJuu.Modules.Identity.Application.Contracts;
using YaaJuu.Modules.Subscriptions.Application.Contracts;
using YaaJuu.Modules.Tenancy.Application.Contracts;

namespace YaaJuu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AdminApiSurfaceTests(YaaJuuApiFactory factory)
{
    [Fact]
    public async Task Login_rejects_invalid_credentials_and_payload()
    {
        using var client = factory.CreateClientUnredirected();
        var invalid = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("nobody@yaajuu.test", "WrongPass!23456"));
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);

        var bad = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("", ""));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Health_endpoints_are_public()
    {
        using var client = factory.CreateClientUnredirected();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Admin_can_query_plans_subscription_audit_and_list()
    {
        using var client = await AuthHelper.AuthenticatedAdminAsync(factory);
        var created = await client.PostAsJsonAsync(
            "/api/v1/admin/franchisees",
            AuthHelper.FranchiseeRequest("SURF1", "owner-surf1@example.com", "900888888"));
        created.EnsureSuccessStatusCode();
        var ids = await created.Content.ReadFromJsonAsync<CreateFranchiseeResponse>(AuthHelper.Json);
        Assert.NotNull(ids);

        var plans = await client.GetFromJsonAsync<IReadOnlyList<PlanDto>>("/api/v1/admin/plans", AuthHelper.Json);
        Assert.NotNull(plans);
        Assert.Contains(plans, p => p.Code == "STANDARD");

        var subscription = await client.GetFromJsonAsync<SubscriptionDto>(
            $"/api/v1/admin/tenants/{ids.TenantId}/subscription",
            AuthHelper.Json);
        Assert.NotNull(subscription);
        Assert.Equal(ids.SubscriptionId, subscription.Id);

        var change = await client.PostAsJsonAsync(
            $"/api/v1/admin/tenants/{ids.TenantId}/subscription/change-plan",
            new ChangePlanRequest("STANDARD"));
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        var list = await client.GetFromJsonAsync<PagedResultDto<FranchiseeListItemDto>>(
            "/api/v1/admin/franchisees?page=1&pageSize=10&search=SURF1",
            AuthHelper.Json);
        Assert.NotNull(list);
        Assert.True(list.TotalCount >= 1);

        var stores = await client.GetFromJsonAsync<IReadOnlyList<StoreDto>>(
            $"/api/v1/admin/franchisees/{ids.FranchiseeId}/stores",
            AuthHelper.Json);
        Assert.NotNull(stores);
        Assert.Single(stores);

        var audit = await client.GetFromJsonAsync<IReadOnlyList<AuditEventDto>>(
            $"/api/v1/admin/audit-events?tenantId={ids.TenantId}",
            AuthHelper.Json);
        Assert.NotNull(audit);
        Assert.NotEmpty(audit);

        var missing = await client.GetAsync($"/api/v1/admin/franchisees/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var missingSubscription = await client.GetAsync($"/api/v1/admin/tenants/{Guid.CreateVersion7()}/subscription");
        Assert.Equal(HttpStatusCode.NotFound, missingSubscription.StatusCode);

        var invalidPlan = await client.PostAsJsonAsync(
            $"/api/v1/admin/tenants/{ids.TenantId}/subscription/change-plan",
            new ChangePlanRequest("NO-SUCH-PLAN"));
        Assert.Equal(HttpStatusCode.NotFound, invalidPlan.StatusCode);

        var invalidCreate = await client.PostAsJsonAsync(
            "/api/v1/admin/franchisees",
            AuthHelper.FranchiseeRequest("SURF2", "bad", "1") with { OwnerEmail = "not-an-email", OwnerPassword = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCreate.StatusCode);
    }

    [Fact]
    public async Task Correlation_id_is_echoed()
    {
        using var client = factory.CreateClientUnredirected();
        var known = Guid.CreateVersion7();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", known.ToString("D"));
        var response = await client.SendAsync(request);
        Assert.Equal(known.ToString("D"), response.Headers.GetValues("X-Correlation-ID").Single());
    }
}
