using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Chevrere.Infrastructure.Audit;
using Chevrere.Infrastructure.Identity;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Consumer.Application.Contracts;
using Chevrere.Modules.Consumer.Domain;
using Chevrere.Modules.Identity.Domain;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace Chevrere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ConsumerServiceAreaTests(ChevrereApiFactory factory)
{
    [Fact]
    public async Task Admin_configures_reads_and_toggles_the_service_area_idempotently()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "SA1", "owner-sa1@example.com", "920100001");

        var missing = await admin.GetAsync($"/api/v1/admin/stores/{store.StoreId}/service-area");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var created = await ConsumerTestData.ConfigureAreaAsync(admin, store.StoreId, 8.7500d, -75.8800d, 3000);
        Assert.False(created.IsEnabled);
        Assert.Equal(store.StoreId, created.StoreId);
        Assert.Equal(store.TenantId, created.TenantId);
        Assert.Equal(3000, created.ServiceRadiusMeters);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.StoreServiceAreaConfigured, created.Id));

        var fetched = await admin.GetFromJsonAsync<AdminServiceAreaDto>(
            $"/api/v1/admin/stores/{store.StoreId}/service-area", AuthHelper.Json);
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);

        var unchanged = await ConsumerTestData.ConfigureAreaAsync(admin, store.StoreId, 8.7500d, -75.8800d, 3000);
        // JSON round-trip can trim sub-millisecond ticks; the audit count is the real no-op signal.
        Assert.Equal(created.UpdatedAt, unchanged.UpdatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.StoreServiceAreaConfigured, created.Id));

        var moved = await ConsumerTestData.ConfigureAreaAsync(admin, store.StoreId, 8.7600d, -75.8800d, 4500);
        Assert.True(moved.UpdatedAt > created.UpdatedAt);
        Assert.Equal(4500, moved.ServiceRadiusMeters);
        Assert.Equal(2, await CountAuditsAsync(AuditActions.StoreServiceAreaConfigured, created.Id));

        await ConsumerTestData.EnableAreaAsync(admin, store.StoreId);
        await ConsumerTestData.EnableAreaAsync(admin, store.StoreId);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.StoreServiceAreaEnabled, created.Id));
        var enabled = await admin.GetFromJsonAsync<AdminServiceAreaDto>(
            $"/api/v1/admin/stores/{store.StoreId}/service-area", AuthHelper.Json);
        Assert.NotNull(enabled);
        Assert.True(enabled.IsEnabled);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PostAsync($"/api/v1/admin/stores/{store.StoreId}/service-area/disable", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PostAsync($"/api/v1/admin/stores/{store.StoreId}/service-area/disable", null)).StatusCode);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.StoreServiceAreaDisabled, created.Id));

        var disabled = await admin.GetFromJsonAsync<AdminServiceAreaDto>(
            $"/api/v1/admin/stores/{store.StoreId}/service-area", AuthHelper.Json);
        Assert.NotNull(disabled);
        Assert.False(disabled.IsEnabled);
        Assert.True(disabled.UpdatedAt > enabled.UpdatedAt);
    }

    [Fact]
    public async Task Enable_and_disable_require_a_configured_area()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "SA2", "owner-sa2@example.com", "920100002");

        var enable = await admin.PostAsync($"/api/v1/admin/stores/{store.StoreId}/service-area/enable", null);
        Assert.Equal(HttpStatusCode.NotFound, enable.StatusCode);
        await AssertProblemTitle(enable, "service_area.not_configured");

        var disable = await admin.PostAsync($"/api/v1/admin/stores/{store.StoreId}/service-area/disable", null);
        Assert.Equal(HttpStatusCode.NotFound, disable.StatusCode);
        await AssertProblemTitle(disable, "service_area.not_configured");
    }

    [Fact]
    public async Task Configuring_an_unknown_store_is_not_found_and_an_invalid_radius_is_rejected()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "SA3", "owner-sa3@example.com", "920100003");

        var unknown = await admin.PutAsJsonAsync(
            $"/api/v1/admin/stores/{Guid.CreateVersion7()}/service-area",
            new ConfigureServiceAreaRequest(8.0d, -75.0d, 3000));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        foreach (var radius in new[] { 99, 50_001 })
        {
            var invalid = await admin.PutAsJsonAsync(
                $"/api/v1/admin/stores/{store.StoreId}/service-area",
                new ConfigureServiceAreaRequest(8.0d, -75.0d, radius));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        var invalidLatitude = await admin.PutAsJsonAsync(
            $"/api/v1/admin/stores/{store.StoreId}/service-area",
            new ConfigureServiceAreaRequest(91d, -75.0d, 3000));
        Assert.Equal(HttpStatusCode.BadRequest, invalidLatitude.StatusCode);
    }

    [Fact]
    public async Task Support_reads_but_cannot_write_and_owner_reads_only_its_own_store()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var mine = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "SA4A", "owner-sa4a@example.com", "920100004");
        var foreign = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "SA4B", "owner-sa4b@example.com", "920100005");

        await ConsumerTestData.ConfigureAreaAsync(admin, mine.StoreId, 8.9000d, -75.9000d, 3000);
        await ConsumerTestData.ConfigureAreaAsync(admin, foreign.StoreId, 8.9500d, -75.9500d, 3000);

        await EnsureSupportUserAsync();
        using var support = factory.CreateClientUnredirected();
        var supportToken = await AuthHelper.LoginAsync(support, SupportEmail, SupportPassword);
        support.DefaultRequestHeaders.Authorization = new("Bearer", supportToken);

        Assert.Equal(
            HttpStatusCode.OK,
            (await support.GetAsync($"/api/v1/admin/stores/{mine.StoreId}/service-area")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await support.PutAsJsonAsync(
                $"/api/v1/admin/stores/{mine.StoreId}/service-area",
                new ConfigureServiceAreaRequest(8.9d, -75.9d, 3500))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await support.PostAsync($"/api/v1/admin/stores/{mine.StoreId}/service-area/enable", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await support.PostAsync($"/api/v1/admin/stores/{mine.StoreId}/service-area/disable", null)).StatusCode);

        using var owner = await ConsumerTestData.OwnerClientAsync(factory, "owner-sa4a@example.com");
        var own = await owner.GetFromJsonAsync<AdminServiceAreaDto>(
            $"/api/v1/business/stores/{mine.StoreId}/service-area", AuthHelper.Json);
        Assert.NotNull(own);
        Assert.Equal(mine.StoreId, own.StoreId);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/v1/business/stores/{foreign.StoreId}/service-area")).StatusCode);

        // The admin surface is staff-only: an owner never reaches the write endpoints at all.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await owner.PutAsJsonAsync(
                $"/api/v1/admin/stores/{mine.StoreId}/service-area",
                new ConfigureServiceAreaRequest(8.9d, -75.9d, 3500))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await owner.PostAsync($"/api/v1/admin/stores/{mine.StoreId}/service-area/enable", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await owner.GetAsync($"/api/v1/admin/stores/{mine.StoreId}/service-area")).StatusCode);
    }

    [Fact]
    public async Task Business_and_admin_service_area_endpoints_reject_anonymous_callers()
    {
        using var anonymous = factory.CreateClientUnredirected();
        var storeId = Guid.CreateVersion7();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/v1/admin/stores/{storeId}/service-area")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/v1/business/stores/{storeId}/service-area")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync(
                $"/api/v1/admin/stores/{storeId}/service-area",
                new ConfigureServiceAreaRequest(8d, -75d, 3000))).StatusCode);
    }

    [Fact]
    public async Task Location_column_keeps_longitude_as_x_and_latitude_as_y()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "SA5", "owner-sa5@example.com", "920100006");
        const double latitude = 4.0000d;
        const double longitude = -73.5000d;
        await ConsumerTestData.ConfigureAreaAsync(admin, store.StoreId, latitude, longitude, 3000);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();

        var area = await db.StoreServiceAreas.SingleAsync(a => a.StoreId == store.StoreId);
        var point = db.Entry(area).Property<Point>("Location").CurrentValue;
        Assert.NotNull(point);
        Assert.Equal(longitude, point.X, 9);
        Assert.Equal(latitude, point.Y, 9);
        Assert.Equal(4326, point.SRID);
    }

    [Fact]
    public async Task Database_rejects_cross_tenant_duplicate_and_out_of_range_service_areas()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var a = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "SA6A", "owner-sa6a@example.com", "920100007");
        var b = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "SA6B", "owner-sa6b@example.com", "920100008");

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();

        // Before any area exists for store A, the composite FK fires first (store A belongs to tenant A).
        var crossTenant = Track(db, BuildArea(b.TenantId, a.StoreId, 9.3d, -75.4d, 3000));
        var fk = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("foreign key", fk.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        db.Entry(crossTenant).State = EntityState.Detached;

        await ConsumerTestData.ConfigureAreaAsync(admin, a.StoreId, 9.3000d, -75.4000d, 3000);

        Track(db, BuildArea(a.TenantId, a.StoreId, 9.3d, -75.4d, 3000));
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("duplicate key", duplicate.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();

        Track(db, BuildArea(b.TenantId, b.StoreId, 9.3d, -75.4d, 99));
        var check = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(
            "ck_store_service_areas_radius_range",
            check.InnerException!.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private const string SupportEmail = "support-consumer@chevrere.test";
    private const string SupportPassword = "SupportTest!23456";

    private static StoreServiceArea Track(ChevrereDbContext db, StoreServiceArea area)
    {
        db.StoreServiceAreas.Add(area);
        db.Entry(area).Property<Point>("Location").CurrentValue =
            new Point(area.Longitude, area.Latitude) { SRID = 4326 };
        return area;
    }

    private static StoreServiceArea BuildArea(
        Guid tenantId,
        Guid storeId,
        double latitude,
        double longitude,
        int radiusMeters)
    {
        var area = (StoreServiceArea)Activator.CreateInstance(typeof(StoreServiceArea), nonPublic: true)!;
        Set(area, nameof(StoreServiceArea.Id), Guid.CreateVersion7());
        Set(area, nameof(StoreServiceArea.TenantId), tenantId);
        Set(area, nameof(StoreServiceArea.StoreId), storeId);
        Set(area, nameof(StoreServiceArea.Latitude), latitude);
        Set(area, nameof(StoreServiceArea.Longitude), longitude);
        Set(area, nameof(StoreServiceArea.ServiceRadiusMeters), radiusMeters);
        Set(area, nameof(StoreServiceArea.IsEnabled), false);
        Set(area, nameof(StoreServiceArea.CreatedAt), DateTimeOffset.UtcNow);
        Set(area, nameof(StoreServiceArea.UpdatedAt), DateTimeOffset.UtcNow);
        return area;
    }

    private static void Set(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(target, value);
    }

    private async Task<int> CountAuditsAsync(string action, Guid entityId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.Set<AuditEvent>().CountAsync(e => e.Action == action && e.EntityId == entityId);
    }

    private async Task EnsureSupportUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(SupportEmail) is not null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = SupportEmail,
            Email = SupportEmail,
            EmailConfirmed = true,
            DisplayName = "Platform Support Consumer",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        Assert.True((await users.CreateAsync(user, SupportPassword)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, RoleNames.PlatformSupport)).Succeeded);
    }

    private static async Task AssertProblemTitle(HttpResponseMessage response, string expected)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(AuthHelper.Json);
        Assert.NotNull(problem);
        Assert.Equal(expected, problem.Title);
    }
}
