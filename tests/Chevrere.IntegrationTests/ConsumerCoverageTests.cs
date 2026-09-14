using System.Net;
using System.Net.Http.Json;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Tenancy.Application.Contracts;
using Chevrere.Modules.Tenancy.Domain;
using Chevrere.SharedKernel.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ConsumerCoverageTests(ChevrereApiFactory factory)
{
    [Fact]
    public async Task Coverage_is_inclusive_at_the_radius_and_false_one_meter_outside()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var anonymous = factory.CreateClientUnredirected();
        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "CV1", "owner-cv1@example.com", "920200001");

        const double centerLat = 4.6000d;
        const double centerLon = -74.1000d;
        const double probeLat = 4.6180d;
        const double probeLon = -74.1000d;

        var distance = await DistanceMetersAsync(centerLat, centerLon, probeLat, probeLon);
        var justEnough = (int)Math.Ceiling(distance);
        var justShort = (int)Math.Floor(distance) - 1;

        await ConsumerTestData.OpenAreaAsync(admin, store.StoreId, centerLat, centerLon, justEnough);
        Assert.True((await ConsumerTestData.CoverageBodyAsync(anonymous, centerLat, centerLon)).ServiceAvailable);
        Assert.True((await ConsumerTestData.CoverageBodyAsync(anonymous, probeLat, probeLon)).ServiceAvailable);

        await ConsumerTestData.ConfigureAreaAsync(admin, store.StoreId, centerLat, centerLon, justShort);
        Assert.False((await ConsumerTestData.CoverageBodyAsync(anonymous, probeLat, probeLon)).ServiceAvailable);
        Assert.True((await ConsumerTestData.CoverageBodyAsync(anonymous, centerLat, centerLon)).ServiceAvailable);
    }

    [Fact]
    public async Task Coverage_requires_an_enabled_area_an_active_store_and_an_active_tenant()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var anonymous = factory.CreateClientUnredirected();
        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "CV2", "owner-cv2@example.com", "920200002");

        const double latitude = -5.4000d;
        const double longitude = -70.2000d;
        await ConsumerTestData.ConfigureAreaAsync(admin, store.StoreId, latitude, longitude, 3000);

        // Configured but never enabled: drawing the area is not opening the store.
        Assert.False((await ConsumerTestData.CoverageBodyAsync(anonymous, latitude, longitude)).ServiceAvailable);

        await ConsumerTestData.EnableAreaAsync(admin, store.StoreId);
        Assert.True((await ConsumerTestData.CoverageBodyAsync(anonymous, latitude, longitude)).ServiceAvailable);

        await SetStoreStatusAsync(store.StoreId, StoreStatus.Suspended);
        Assert.False((await ConsumerTestData.CoverageBodyAsync(anonymous, latitude, longitude)).ServiceAvailable);

        await SetStoreStatusAsync(store.StoreId, StoreStatus.Active);
        await SetTenantStatusAsync(store.TenantId, TenantStatus.Suspended);
        Assert.False((await ConsumerTestData.CoverageBodyAsync(anonymous, latitude, longitude)).ServiceAvailable);

        await SetTenantStatusAsync(store.TenantId, TenantStatus.Active);
        Assert.True((await ConsumerTestData.CoverageBodyAsync(anonymous, latitude, longitude)).ServiceAvailable);

        // Suspending the associate suspends tenant and stores at once: coverage disappears.
        (await admin.PostAsJsonAsync(
            $"/api/v1/admin/franchisees/{store.FranchiseeId}/suspend",
            new SuspendFranchiseeRequest("Prueba de cobertura")))
            .EnsureSuccessStatusCode();
        Assert.False((await ConsumerTestData.CoverageBodyAsync(anonymous, latitude, longitude)).ServiceAvailable);
    }

    [Fact]
    public async Task Coverage_is_false_where_no_store_reaches()
    {
        using var anonymous = factory.CreateClientUnredirected();

        var body = await ConsumerTestData.CoverageBodyAsync(anonymous, -40.1234d, 150.5678d);

        Assert.False(body.ServiceAvailable);
    }

    [Fact]
    public async Task Coverage_rejects_coordinates_outside_the_wgs84_range()
    {
        using var anonymous = factory.CreateClientUnredirected();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await ConsumerTestData.CoverageAsync(anonymous, 91d, 0d)).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await ConsumerTestData.CoverageAsync(anonymous, 0d, -181d)).StatusCode);
    }

    /// <summary>
    /// Two eligible stores, each offering a different product. The catalog the consumer receives is
    /// the only observable way to tell which one was picked; the response itself never names it.
    /// </summary>
    [Fact]
    public async Task The_nearest_eligible_store_serves_the_consumer()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var anonymous = factory.CreateClientUnredirected();
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-CVN", "Bebidas Nearest");
        var productNear = await CatalogAdminTests.CreateProductAsync(
            admin, category.Id, "SKU-CVN-NEAR", "Gaseosa Cercana", null);
        var productFar = await CatalogAdminTests.CreateProductAsync(
            admin, category.Id, "SKU-CVN-FAR", "Gaseosa Lejana", null);
        await ConsumerTestData.SetGlobalPriceAsync(admin, productNear.Id, 5000m);
        await ConsumerTestData.SetGlobalPriceAsync(admin, productFar.Id, 5000m);

        var near = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "CVNA", "owner-cvna@example.com", "920200003");
        var far = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "CVNB", "owner-cvnb@example.com", "920200004");

        const double consumerLat = 1.0000d;
        const double consumerLon = -74.0000d;
        await ConsumerTestData.OpenAreaAsync(admin, near.StoreId, consumerLat, consumerLon, 10_000);
        await ConsumerTestData.OpenAreaAsync(admin, far.StoreId, 1.0300d, consumerLon, 10_000);

        using var ownerNear = await ConsumerTestData.OwnerClientAsync(factory, "owner-cvna@example.com");
        using var ownerFar = await ConsumerTestData.OwnerClientAsync(factory, "owner-cvnb@example.com");
        await ConsumerTestData.StockAndPriceAsync(ownerNear, near.StoreId, productNear.Id, 10, "cvn-near-0001");
        await ConsumerTestData.StockAndPriceAsync(ownerFar, far.StoreId, productFar.Id, 10, "cvn-far-0001");

        var atNear = await ConsumerTestData.SearchAsync(anonymous, consumerLat, consumerLon);
        Assert.True(atNear.ServiceAvailable);
        Assert.Equal(productNear.Id, Assert.Single(atNear.Items).Id);

        // Standing next to the other store flips the answer, proving the resolver is distance based.
        var atFar = await ConsumerTestData.SearchAsync(anonymous, 1.0300d, consumerLon);
        Assert.Equal(productFar.Id, Assert.Single(atFar.Items).Id);
    }

    /// <summary>
    /// Distance alone does not decide: a closer store whose radius falls short loses against a
    /// farther store that actually reaches the consumer.
    /// </summary>
    [Fact]
    public async Task A_farther_store_wins_when_the_closer_one_does_not_reach()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var anonymous = factory.CreateClientUnredirected();
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-CVR", "Bebidas Radios");
        var productClose = await CatalogAdminTests.CreateProductAsync(
            admin, category.Id, "SKU-CVR-CLOSE", "Gaseosa Corta", null);
        var productFar = await CatalogAdminTests.CreateProductAsync(
            admin, category.Id, "SKU-CVR-FAR", "Gaseosa Larga", null);
        await ConsumerTestData.SetGlobalPriceAsync(admin, productClose.Id, 5000m);
        await ConsumerTestData.SetGlobalPriceAsync(admin, productFar.Id, 5000m);

        var close = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "CVRA", "owner-cvra@example.com", "920200005");
        var far = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "CVRB", "owner-cvrb@example.com", "920200006");

        const double consumerLat = -2.0000d;
        const double consumerLon = -73.0000d;
        await ConsumerTestData.OpenAreaAsync(admin, close.StoreId, -2.0090d, consumerLon, 500);
        await ConsumerTestData.OpenAreaAsync(admin, far.StoreId, -2.0360d, consumerLon, 10_000);

        using var ownerClose = await ConsumerTestData.OwnerClientAsync(factory, "owner-cvra@example.com");
        using var ownerFar = await ConsumerTestData.OwnerClientAsync(factory, "owner-cvrb@example.com");
        await ConsumerTestData.StockAndPriceAsync(ownerClose, close.StoreId, productClose.Id, 10, "cvr-close-0001");
        await ConsumerTestData.StockAndPriceAsync(ownerFar, far.StoreId, productFar.Id, 10, "cvr-far-00001");

        var catalog = await ConsumerTestData.SearchAsync(anonymous, consumerLat, consumerLon);

        Assert.True(catalog.ServiceAvailable);
        Assert.Equal(productFar.Id, Assert.Single(catalog.Items).Id);
    }

    /// <summary>
    /// Repeated discovery calls must be a pure read: same answer, and not one row written anywhere.
    /// </summary>
    [Fact]
    public async Task Repeated_discovery_calls_are_deterministic_and_store_no_consumer_location()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var anonymous = factory.CreateClientUnredirected();
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-CVD", "Bebidas Determinismo");
        var product = await CatalogAdminTests.CreateProductAsync(
            admin, category.Id, "SKU-CVD-1", "Gaseosa Determinista", null);
        await ConsumerTestData.SetGlobalPriceAsync(admin, product.Id, 5000m);

        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "CVD1", "owner-cvd1@example.com", "920200007");
        const double latitude = 2.5000d;
        const double longitude = -72.5000d;
        await ConsumerTestData.OpenAreaAsync(admin, store.StoreId, latitude, longitude, 5000);

        using var owner = await ConsumerTestData.OwnerClientAsync(factory, "owner-cvd1@example.com");
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, product.Id, 10, "cvd-init-0001");

        var before = await TotalRowsAsync();
        for (var i = 0; i < 10; i++)
        {
            Assert.True((await ConsumerTestData.CoverageBodyAsync(anonymous, latitude, longitude)).ServiceAvailable);
            var catalog = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude);
            Assert.True(catalog.ServiceAvailable);
            Assert.Equal(1, catalog.TotalCount);
            Assert.Equal(product.Id, Assert.Single(catalog.Items).Id);
        }

        Assert.Equal(before, await TotalRowsAsync());
        Assert.Empty(await LocationLikeTablesAsync());
    }

    private async Task<double> DistanceMetersAsync(double lat1, double lon1, double lat2, double lon2)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.Database
            .SqlQuery<double>($"""
                SELECT ST_Distance(
                    ST_SetSRID(ST_MakePoint({lon1}, {lat1}), 4326)::geography,
                    ST_SetSRID(ST_MakePoint({lon2}, {lat2}), 4326)::geography) AS "Value"
                """)
            .SingleAsync();
    }

    private async Task SetStoreStatusAsync(Guid storeId, StoreStatus status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        var store = await db.Stores.SingleAsync(s => s.Id == storeId);
        if (status == StoreStatus.Active)
        {
            store.Activate(DateTimeOffset.UtcNow);
        }
        else
        {
            store.Suspend(DateTimeOffset.UtcNow);
        }

        await db.SaveChangesAsync();
    }

    private async Task SetTenantStatusAsync(Guid tenantId, TenantStatus status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);
        if (status == TenantStatus.Active)
        {
            tenant.Activate(DateTimeOffset.UtcNow);
        }
        else
        {
            tenant.Suspend(DateTimeOffset.UtcNow);
        }

        await db.SaveChangesAsync();
    }

    private async Task<long> TotalRowsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.Database
            .SqlQuery<long>($"""
                SELECT (
                    (SELECT COUNT(*) FROM audit_events)
                    + (SELECT COUNT(*) FROM store_service_areas)
                    + (SELECT COUNT(*) FROM inventory_movements)
                    + (SELECT COUNT(*) FROM idempotent_operations)
                    + (SELECT COUNT(*) FROM store_product_prices)
                    + (SELECT COUNT(*) FROM store_products)) AS "Value"
                """)
            .SingleAsync();
    }

    private async Task<List<string>> LocationLikeTablesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.Database
            .SqlQuery<string>($"""
                SELECT table_name::text AS "Value"
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_type = 'BASE TABLE'
                  AND table_name <> 'store_service_areas'
                  AND table_name NOT IN ('geography_columns', 'geometry_columns', 'spatial_ref_sys')
                  AND (table_name LIKE '%location%'
                       OR table_name LIKE '%coordinate%'
                       OR table_name LIKE '%consumer%'
                       OR table_name LIKE '%geo%')
                """)
            .ToListAsync();
    }
}
