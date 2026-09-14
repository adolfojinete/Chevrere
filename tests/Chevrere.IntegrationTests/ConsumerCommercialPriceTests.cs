using System.Net;
using System.Net.Http.Json;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Consumer.Application.Contracts;
using Chevrere.Modules.Pricing.Domain;
using Chevrere.SharedKernel.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.IntegrationTests;

/// <summary>
/// EffectivePrice is part of commercial visibility, not a later enrichment. These cases pin the
/// single SQL composition: override vs suggested, historical rows, count/page, categories, detail.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ConsumerCommercialPriceTests(ChevrereApiFactory factory)
{
    [Fact]
    public async Task Removing_the_store_override_falls_back_to_the_current_global_price()
    {
        var ctx = await SeedPricedStoreAsync("CFLB", 6.2500d, -75.5800d);
        await ConsumerTestData.SetStorePriceAsync(ctx.Owner, ctx.StoreId, ctx.PricedProductId, 6500m);

        var withOverride = await ConsumerTestData.SearchAsync(ctx.Anonymous, ctx.Latitude, ctx.Longitude, pageSize: 10);
        AssertCommerciallyPriced(Assert.Single(withOverride.Items, i => i.Id == ctx.PricedProductId), 6500m);

        await ConsumerTestData.RemoveStorePriceAsync(ctx.Owner, ctx.StoreId, ctx.PricedProductId);

        var afterRemove = await ConsumerTestData.SearchAsync(ctx.Anonymous, ctx.Latitude, ctx.Longitude, pageSize: 10);
        AssertCommerciallyPriced(Assert.Single(afterRemove.Items, i => i.Id == ctx.PricedProductId), 6000m);
    }

    [Fact]
    public async Task A_closed_store_override_does_not_beat_the_current_global_price()
    {
        var ctx = await SeedPricedStoreAsync("CHST", 4.6100d, -74.0800d);
        await ConsumerTestData.SetStorePriceAsync(ctx.Owner, ctx.StoreId, ctx.PricedProductId, 6500m);
        await ConsumerTestData.RemoveStorePriceAsync(ctx.Owner, ctx.StoreId, ctx.PricedProductId);

        var catalog = await ConsumerTestData.SearchAsync(ctx.Anonymous, ctx.Latitude, ctx.Longitude, pageSize: 10);
        AssertCommerciallyPriced(Assert.Single(catalog.Items, i => i.Id == ctx.PricedProductId), 6000m);
    }

    [Fact]
    public async Task A_closed_global_price_does_not_hide_a_current_store_override()
    {
        var ctx = await SeedPricedStoreAsync("CHGL", 4.4400d, -75.2400d);
        await ConsumerTestData.SetStorePriceAsync(ctx.Owner, ctx.StoreId, ctx.PricedProductId, 6500m);
        await CloseCurrentGlobalPriceAsync(ctx.PricedProductId);

        var catalog = await ConsumerTestData.SearchAsync(ctx.Anonymous, ctx.Latitude, ctx.Longitude, pageSize: 10);
        AssertCommerciallyPriced(Assert.Single(catalog.Items, i => i.Id == ctx.PricedProductId), 6500m);
    }

    [Fact]
    public async Task Historical_prices_alone_do_not_make_a_product_commercially_visible()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var anonymous = factory.CreateClientUnredirected();
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-CHIS", "Bebidas Historico");
        var product = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-CHIS-1", "Solo Historico", null);
        await ConsumerTestData.SetGlobalPriceAsync(admin, product.Id, 6000m);

        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "CHIS", "owner-chis@example.com", "920300010");
        const double latitude = 1.2100d;
        const double longitude = -77.2800d;
        await ConsumerTestData.OpenAreaAsync(admin, store.StoreId, latitude, longitude, 5000);
        using var owner = await ConsumerTestData.OwnerClientAsync(factory, "owner-chis@example.com");
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, product.Id, 10, "chis-init-001");
        await CloseCurrentGlobalPriceAsync(product.Id);

        var catalog = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude, pageSize: 20);
        Assert.DoesNotContain(catalog.Items, i => i.Id == product.Id);
        Assert.Equal(0, catalog.TotalCount);

        var categories = await ConsumerTestData.CategoriesAsync(anonymous, latitude, longitude);
        Assert.Empty(categories.Items);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ConsumerTestData.ProductAsync(anonymous, product.Id, latitude, longitude)).StatusCode);
    }

    [Fact]
    public async Task Products_without_a_current_effective_price_are_absent_from_count_pages_categories_and_detail()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var anonymous = factory.CreateClientUnredirected();
        var pricedCategory = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-CNPR", "Bebidas Con Precio");
        var unpricedCategory = await CatalogAdminTests.CreateCategoryAsync(admin, "SNK-CNPR", "Snacks Sin Precio");

        var first = await CatalogAdminTests.CreateProductAsync(admin, pricedCategory.Id, "SKU-CNPR-A", "Alpha Priced", null);
        var second = await CatalogAdminTests.CreateProductAsync(admin, pricedCategory.Id, "SKU-CNPR-B", "Beta Priced", null);
        var unpriced = await CatalogAdminTests.CreateProductAsync(
            admin, unpricedCategory.Id, "SKU-CNPR-C", "Gamma Sin Precio", null);

        await ConsumerTestData.SetGlobalPriceAsync(admin, first.Id, 6000m);
        await ConsumerTestData.SetGlobalPriceAsync(admin, second.Id, 7000m);

        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "CNPR", "owner-cnpr@example.com", "920300011");
        const double latitude = 8.7500d;
        const double longitude = -75.8800d;
        await ConsumerTestData.OpenAreaAsync(admin, store.StoreId, latitude, longitude, 5000);
        using var owner = await ConsumerTestData.OwnerClientAsync(factory, "owner-cnpr@example.com");

        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, first.Id, 10, "cnpr-a-000001");
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, second.Id, 10, "cnpr-b-000001");
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, unpriced.Id, 10, "cnpr-c-000001");

        var catalog = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude, pageSize: 100);
        Assert.Equal(2, catalog.TotalCount);
        Assert.Equal(2, catalog.Items.Count);
        Assert.DoesNotContain(catalog.Items, i => i.Id == unpriced.Id);
        Assert.All(catalog.Items, item =>
        {
            Assert.True(item.Price > 0m);
            Assert.Equal("COP", item.Currency);
        });

        var page1 = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude, page: 1, pageSize: 1);
        var page2 = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude, page: 2, pageSize: 1);
        var page3 = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude, page: 3, pageSize: 1);
        Assert.Equal(2, page1.TotalCount);
        Assert.Equal(2, page2.TotalCount);
        Assert.Single(page1.Items);
        Assert.Single(page2.Items);
        Assert.Empty(page3.Items);
        Assert.Equal(
            catalog.Items.Select(i => i.Id),
            new[] { page1.Items[0].Id, page2.Items[0].Id });

        var categories = await ConsumerTestData.CategoriesAsync(anonymous, latitude, longitude);
        Assert.Equal(pricedCategory.Id, Assert.Single(categories.Items).Id);

        var pricedDetail = await ConsumerTestData.ProductAsync(anonymous, first.Id, latitude, longitude);
        pricedDetail.EnsureSuccessStatusCode();
        var detail = await pricedDetail.Content.ReadFromJsonAsync<ConsumerProductDetailDto>(AuthHelper.Json);
        Assert.NotNull(detail);
        Assert.True(detail.Price > 0m);
        Assert.False(string.IsNullOrWhiteSpace(detail.Currency));

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ConsumerTestData.ProductAsync(anonymous, unpriced.Id, latitude, longitude)).StatusCode);
    }

    private async Task<PricedStoreContext> SeedPricedStoreAsync(string suffix, double latitude, double longitude)
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var anonymous = factory.CreateClientUnredirected();
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, $"BEB-{suffix}", $"Bebidas {suffix}");
        var product = await CatalogAdminTests.CreateProductAsync(
            admin, category.Id, $"SKU-{suffix}-1", $"Producto {suffix}", null);
        await ConsumerTestData.SetGlobalPriceAsync(admin, product.Id, 6000m);

        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, suffix, $"owner-{suffix.ToLowerInvariant()}@example.com", NextIdentification(suffix));
        await ConsumerTestData.OpenAreaAsync(admin, store.StoreId, latitude, longitude, 5000);
        var owner = await ConsumerTestData.OwnerClientAsync(factory, $"owner-{suffix.ToLowerInvariant()}@example.com");
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, product.Id, 10, $"{suffix.ToLowerInvariant()}-init-001");

        return new PricedStoreContext(
            anonymous,
            owner,
            store.StoreId,
            product.Id,
            latitude,
            longitude);
    }

    private static string NextIdentification(string suffix) =>
        suffix switch
        {
            "CFLB" => "920300007",
            "CHST" => "920300008",
            "CHGL" => "920300009",
            _ => throw new ArgumentOutOfRangeException(nameof(suffix), suffix, null)
        };

    private async Task CloseCurrentGlobalPriceAsync(Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        var current = await db.GlobalProductPrices.SingleAsync(p => p.GlobalProductId == productId && p.ValidTo == null);
        current.Close(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private static void AssertCommerciallyPriced(ConsumerProductListItemDto item, decimal expectedPrice)
    {
        Assert.Equal(expectedPrice, item.Price);
        Assert.Equal("COP", item.Currency);
        Assert.True(item.Price > 0m);
        Assert.False(string.IsNullOrWhiteSpace(item.Currency));
    }

    private sealed record PricedStoreContext(
        HttpClient Anonymous,
        HttpClient Owner,
        Guid StoreId,
        Guid PricedProductId,
        double Latitude,
        double Longitude);
}
