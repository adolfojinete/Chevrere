using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chevrere.Modules.Subscriptions.Domain;
using Chevrere.Modules.Tenancy.Application.Contracts;
using Chevrere.Modules.Tenancy.Domain;

namespace Chevrere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class OnboardingTests(ChevrereApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = AuthHelper.Json;

    [Fact]
    public async Task Create_franchisee_creates_tenant_owner_store_and_subscription()
    {
        using var client = await AuthHelper.AuthenticatedAdminAsync(factory);
        var request = AuthHelper.FranchiseeRequest("ONB1", "owner-onb1@example.com", "900111111");

        var create = await client.PostAsJsonAsync("/api/v1/admin/franchisees", request);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<CreateFranchiseeResponse>(Json);
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.TenantId);
        Assert.NotEqual(Guid.Empty, created.FranchiseeId);
        Assert.NotEqual(Guid.Empty, created.StoreId);
        Assert.NotEqual(Guid.Empty, created.OwnerUserId);
        Assert.NotEqual(Guid.Empty, created.SubscriptionId);

        var detailResponse = await client.GetAsync($"/api/v1/admin/franchisees/{created.FranchiseeId}");
        detailResponse.EnsureSuccessStatusCode();
        var detail = await detailResponse.Content.ReadFromJsonAsync<FranchiseeDetailDto>(Json);
        Assert.NotNull(detail);
        Assert.Equal(FranchiseeStatus.Pending, detail.Status);
        Assert.Equal(created.TenantId, detail.TenantId);
        Assert.Equal("OP-ONB1", detail.TenantCode);
        Assert.NotNull(detail.Owner);
        Assert.Equal("owner-onb1@example.com", detail.Owner.Email);
        Assert.NotNull(detail.Subscription);
        Assert.Equal("STANDARD", detail.Subscription.PlanCode);
        Assert.Equal(SubscriptionStatus.Trial, detail.Subscription.Status);
        Assert.Single(detail.Stores);
        Assert.Equal(created.StoreId, detail.Stores[0].Id);
        Assert.Equal(created.TenantId, detail.Stores[0].TenantId);
        Assert.Equal(created.FranchiseeId, detail.Stores[0].FranchiseeId);
    }

    [Fact]
    public async Task Duplicate_identification_does_not_create_partial_data()
    {
        using var client = await AuthHelper.AuthenticatedAdminAsync(factory);
        var first = AuthHelper.FranchiseeRequest("DUP1", "owner-dup1@example.com", "900222222");
        var second = AuthHelper.FranchiseeRequest("DUP2", "owner-dup2@example.com", "900222222");

        var created = await client.PostAsJsonAsync("/api/v1/admin/franchisees", first);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var conflict = await client.PostAsJsonAsync("/api/v1/admin/franchisees", second);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var list = await client.GetFromJsonAsync<PagedResultDto<FranchiseeListItemDto>>(
            "/api/v1/admin/franchisees?search=DUP2",
            Json);
        Assert.NotNull(list);
        Assert.Equal(0, list.TotalCount);
    }

    [Fact]
    public async Task Invalid_plan_does_not_leave_orphan_tenant()
    {
        using var client = await AuthHelper.AuthenticatedAdminAsync(factory);
        var request = AuthHelper.FranchiseeRequest("BADPLAN", "owner-badplan@example.com", "900333333")
            with { PlanCode = "DOES-NOT-EXIST" };

        var response = await client.PostAsJsonAsync("/api/v1/admin/franchisees", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var list = await client.GetFromJsonAsync<PagedResultDto<FranchiseeListItemDto>>(
            "/api/v1/admin/franchisees?search=BADPLAN",
            Json);
        Assert.NotNull(list);
        Assert.Equal(0, list.TotalCount);
    }
}

internal sealed record PagedResultDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
