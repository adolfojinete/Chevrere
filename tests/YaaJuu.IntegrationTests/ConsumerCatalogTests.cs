using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Consumer.Application.Contracts;
using YaaJuu.Modules.Inventory.Domain;
using YaaJuu.SharedKernel.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ConsumerCatalogTests(YaaJuuApiFactory factory)
{
    /// <summary>
    /// A product reaches the consumer only when every commercial condition holds at once. Each
    /// product below breaks exactly one of them.
    /// </summary>
    [Fact]
    public async Task Only_products_that_pass_every_commercial_condition_are_visible()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var anonymous = factory.CreateClientUnredirected();
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-CVIS", "Bebidas Visibilidad");
        var otherCategory = await CatalogAdminTests.CreateCategoryAsync(admin, "SNK-CVIS", "Snacks Visibilidad");

        var happy = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-CVIS-OK", "Visible OK", null);
        var noPrice = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-CVIS-NOPRICE", "Sin Precio", null);
        var noStockRow = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-CVIS-NOINV", "Sin Inventario", null);
        var zeroStock = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-CVIS-ZERO", "Sin Disponible", null);
        var reservedOut = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-CVIS-RESERVED", "Todo Reservado", null);
        var disabledOffer = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-CVIS-OFF", "Oferta Apagada", null);
        var inactiveProduct = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-CVIS-INACT", "Producto Inactivo", null);
        var inactiveCategory = await CatalogAdminTests.CreateProductAsync(admin, otherCategory.Id, "SKU-CVIS-CAT", "Categoria Inactiva", null);

        foreach (var priced in new[] { happy, noStockRow, zeroStock, reservedOut, disabledOffer, inactiveProduct, inactiveCategory })
        {
            await ConsumerTestData.SetGlobalPriceAsync(admin, priced.Id, 5000m);
        }

        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "CVIS", "owner-cvis@example.com", "920300001");
        const double latitude = 6.2000d;
        const double longitude = -75.6000d;
        await ConsumerTestData.OpenAreaAsync(admin, store.StoreId, latitude, longitude, 5000);
        using var owner = await ConsumerTestData.OwnerClientAsync(factory, "owner-cvis@example.com");

        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, happy.Id, 10, "cvis-ok-0001");
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, noPrice.Id, 10, "cvis-noprice1");
        await ConsumerTestData.OfferProductAsync(owner, store.StoreId, noStockRow.Id);
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, zeroStock.Id, 0, "cvis-zero-001");
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, reservedOut.Id, 5, "cvis-reserved1");
        await ReserveEverythingAsync(store.StoreId, reservedOut.Id);

        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, disabledOffer.Id, 10, "cvis-off-0001");
        (await owner.PostAsync(
            $"/api/v1/business/stores/{store.StoreId}/products/{disabledOffer.Id}/disable", null))
            .EnsureSuccessStatusCode();

        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, inactiveProduct.Id, 10, "cvis-inact-01");
        (await admin.PostAsync($"/api/v1/admin/products/{inactiveProduct.Id}/deactivate", null))
            .EnsureSuccessStatusCode();

        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, inactiveCategory.Id, 10, "cvis-cat-0001");
        (await admin.PostAsync($"/api/v1/admin/categories/{otherCategory.Id}/deactivate", null))
            .EnsureSuccessStatusCode();

        var catalog = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude, pageSize: 100);

        Assert.True(catalog.ServiceAvailable);
        Assert.Equal(happy.Id, Assert.Single(catalog.Items).Id);
        Assert.Equal(1, catalog.TotalCount);
    }

    [Fact]
    public async Task The_store_override_beats_the_global_suggested_price()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var anonymous = factory.CreateClientUnredirected();
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-CPRC", "Bebidas Precio");
        var suggestedOnly = await CatalogAdminTests.CreateProductAsync(
            admin, category.Id, "SKU-CPRC-GLOBAL", "Precio Global", null);
        var overridden = await CatalogAdminTests.CreateProductAsync(
            admin, category.Id, "SKU-CPRC-STORE", "Precio Store", null);
        await ConsumerTestData.SetGlobalPriceAsync(admin, suggestedOnly.Id, 6000m);
        await ConsumerTestData.SetGlobalPriceAsync(admin, overridden.Id, 6000m);

        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "CPRC", "owner-cprc@example.com", "920300002");
        const double latitude = 5.0700d;
        const double longitude = -75.5200d;
        await ConsumerTestData.OpenAreaAsync(admin, store.StoreId, latitude, longitude, 5000);

        using var owner = await ConsumerTestData.OwnerClientAsync(factory, "owner-cprc@example.com");
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, suggestedOnly.Id, 10, "cprc-global01");
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, overridden.Id, 10, "cprc-store-01", 6500m);

        var catalog = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude, pageSize: 100);

        var global = Assert.Single(catalog.Items, i => i.Id == suggestedOnly.Id);
        var store2 = Assert.Single(catalog.Items, i => i.Id == overridden.Id);
        Assert.Equal(6000m, global.Price);
        Assert.Equal("COP", global.Currency);
        Assert.Equal(6500m, store2.Price);
        Assert.Equal("COP", store2.Currency);
        Assert.All(catalog.Items, i =>
        {
            Assert.True(i.Price > 0m);
            Assert.False(string.IsNullOrWhiteSpace(i.Currency));
        });
    }

    [Fact]
    public async Task Search_filters_by_text_and_category_and_pages_stably()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var anonymous = factory.CreateClientUnredirected();
        var drinks = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-CSCH", "Bebidas Busqueda");
        var snacks = await CatalogAdminTests.CreateCategoryAsync(admin, "SNK-CSCH", "Snacks Busqueda");

        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "CSCH", "owner-csch@example.com", "920300003");
        const double latitude = 10.4000d;
        const double longitude = -75.5000d;
        await ConsumerTestData.OpenAreaAsync(admin, store.StoreId, latitude, longitude, 5000);
        using var owner = await ConsumerTestData.OwnerClientAsync(factory, "owner-csch@example.com");

        var names = new[]
        {
            ("SKU-CSCH-1", "Coca-Cola 1.5 L", drinks.Id),
            ("SKU-CSCH-2", "Coca-Cola 350 ml", drinks.Id),
            ("SKU-CSCH-3", "Agua Cristal 600 ml", drinks.Id),
            ("SKU-CSCH-4", "Papas Margarita", snacks.Id),
            ("SKU-CSCH-5", "Zuma Naranja", drinks.Id)
        };

        var created = new List<Guid>();
        foreach (var (sku, name, categoryId) in names)
        {
            var product = await CatalogAdminTests.CreateProductAsync(admin, categoryId, sku, name, null);
            await ConsumerTestData.SetGlobalPriceAsync(admin, product.Id, 5000m);
            await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, product.Id, 10, $"csch-{sku}");
            created.Add(product.Id);
        }

        var coca = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude, search: "Coca", pageSize: 100);
        Assert.Equal(2, coca.TotalCount);
        Assert.All(coca.Items, i => Assert.StartsWith("Coca-Cola", i.Name, StringComparison.Ordinal));

        var lowercase = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude, search: "coca", pageSize: 100);
        Assert.Equal(2, lowercase.TotalCount);

        var bySku = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude, search: "CSCH-4", pageSize: 100);
        Assert.Equal("Papas Margarita", Assert.Single(bySku.Items).Name);

        var onlySnacks = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude, categoryId: snacks.Id, pageSize: 100);
        Assert.Equal("Papas Margarita", Assert.Single(onlySnacks.Items).Name);

        var all = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude, pageSize: 100);
        Assert.Equal(5, all.TotalCount);
        Assert.Equal(created.Count, all.Items.Count);

        var expectedOrder = all.Items.Select(i => i.Id).ToList();
        var paged = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var slice = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude, page: page, pageSize: 2);
            Assert.Equal(5, slice.TotalCount);
            Assert.Equal(page, slice.Page);
            Assert.Equal(2, slice.PageSize);
            paged.AddRange(slice.Items.Select(i => i.Id));
        }

        Assert.Equal(expectedOrder, paged);

        // pageSize is clamped, never trusted.
        var clamped = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude, page: 0, pageSize: 5000);
        Assert.Equal(1, clamped.Page);
        Assert.Equal(100, clamped.PageSize);
    }

    [Fact]
    public async Task Categories_only_list_those_with_a_visible_product()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var anonymous = factory.CreateClientUnredirected();
        var withStock = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-CCAT", "Bebidas Categorias");
        var withoutStock = await CatalogAdminTests.CreateCategoryAsync(admin, "SNK-CCAT", "Snacks Categorias");

        var visible = await CatalogAdminTests.CreateProductAsync(admin, withStock.Id, "SKU-CCAT-1", "Bebida Visible", null);
        var hidden = await CatalogAdminTests.CreateProductAsync(admin, withoutStock.Id, "SKU-CCAT-2", "Snack Sin Stock", null);
        await ConsumerTestData.SetGlobalPriceAsync(admin, visible.Id, 5000m);
        await ConsumerTestData.SetGlobalPriceAsync(admin, hidden.Id, 5000m);

        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "CCAT", "owner-ccat@example.com", "920300004");
        const double latitude = 3.4000d;
        const double longitude = -76.5000d;
        await ConsumerTestData.OpenAreaAsync(admin, store.StoreId, latitude, longitude, 5000);

        using var owner = await ConsumerTestData.OwnerClientAsync(factory, "owner-ccat@example.com");
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, visible.Id, 10, "ccat-init-001");
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, hidden.Id, 0, "ccat-init-002");

        var categories = await ConsumerTestData.CategoriesAsync(anonymous, latitude, longitude);

        Assert.True(categories.ServiceAvailable);
        var only = Assert.Single(categories.Items);
        Assert.Equal(withStock.Id, only.Id);
        Assert.Equal("Bebidas Categorias", only.Name);

        var uncovered = await ConsumerTestData.CategoriesAsync(anonymous, -33.4500d, 18.5000d);
        Assert.False(uncovered.ServiceAvailable);
        Assert.Empty(uncovered.Items);
    }

    [Fact]
    public async Task Product_detail_is_served_only_where_the_product_is_buyable()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var anonymous = factory.CreateClientUnredirected();
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-CDET", "Bebidas Detalle");
        var visible = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-CDET-1", "Detalle Visible", null);
        var unstocked = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-CDET-2", "Detalle Sin Stock", null);
        await ConsumerTestData.SetGlobalPriceAsync(admin, visible.Id, 7200m);
        await ConsumerTestData.SetGlobalPriceAsync(admin, unstocked.Id, 7200m);

        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "CDET", "owner-cdet@example.com", "920300005");
        const double latitude = 7.1000d;
        const double longitude = -73.1000d;
        await ConsumerTestData.OpenAreaAsync(admin, store.StoreId, latitude, longitude, 5000);

        using var owner = await ConsumerTestData.OwnerClientAsync(factory, "owner-cdet@example.com");
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, visible.Id, 10, "cdet-init-001");
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, unstocked.Id, 0, "cdet-init-002");

        var found = await ConsumerTestData.ProductAsync(anonymous, visible.Id, latitude, longitude);
        found.EnsureSuccessStatusCode();
        var detail = await found.Content.ReadFromJsonAsync<ConsumerProductDetailDto>(AuthHelper.Json);
        Assert.NotNull(detail);
        Assert.Equal(visible.Id, detail.Id);
        Assert.Equal("Detalle Visible", detail.Name);
        Assert.Equal(7200m, detail.Price);
        Assert.Equal(category.Id, detail.CategoryId);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ConsumerTestData.ProductAsync(anonymous, unstocked.Id, latitude, longitude)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ConsumerTestData.ProductAsync(anonymous, Guid.CreateVersion7(), latitude, longitude)).StatusCode);

        // Same product, no coverage: indistinguishable from a product that does not exist.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ConsumerTestData.ProductAsync(anonymous, visible.Id, -33.4500d, 18.5000d)).StatusCode);
    }

    /// <summary>
    /// The consumer payload is the privacy boundary of the whole dark store model: one leaked field
    /// and the single-brand illusion is over.
    /// </summary>
    [Fact]
    public async Task Consumer_payloads_never_leak_store_tenant_location_or_stock()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var anonymous = factory.CreateClientUnredirected();
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-CPRV", "Bebidas Privacidad");
        var product = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-CPRV-1", "Privada", null);
        await ConsumerTestData.SetGlobalPriceAsync(admin, product.Id, 5000m);

        var store = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "CPRV", "owner-cprv@example.com", "920300006");
        const double latitude = 11.0000d;
        const double longitude = -74.8000d;
        await ConsumerTestData.OpenAreaAsync(admin, store.StoreId, latitude, longitude, 5000);

        using var owner = await ConsumerTestData.OwnerClientAsync(factory, "owner-cprv@example.com");
        await ConsumerTestData.StockAndPriceAsync(owner, store.StoreId, product.Id, 42, "cprv-init-001");

        var payloads = new List<string>
        {
            await ReadBodyAsync(await ConsumerTestData.CoverageAsync(anonymous, latitude, longitude)),
            await ReadBodyAsync(await anonymous.PostAsJsonAsync(
                "/api/v1/consumer/catalog/search",
                new ConsumerCatalogSearchRequest
                {
                    Latitude = latitude,
                    Longitude = longitude,
                    Page = 1,
                    PageSize = 20
                })),
            await ReadBodyAsync(await anonymous.PostAsJsonAsync(
                "/api/v1/consumer/categories",
                new ConsumerLocationRequest(latitude, longitude))),
            await ReadBodyAsync(await ConsumerTestData.ProductAsync(anonymous, product.Id, latitude, longitude))
        };

        string[] forbidden =
        [
            "storeId", "tenantId", "serviceAreaId", "franchiseeId", "latitude", "longitude",
            "address", "distance", "radius", "onHand", "reserved", "stock", "cost"
        ];

        foreach (var payload in payloads)
        {
            foreach (var field in forbidden)
            {
                Assert.DoesNotContain(field, payload, StringComparison.OrdinalIgnoreCase);
            }
        }

        // The store id is not hidden behind a different name either.
        Assert.All(payloads, p => Assert.DoesNotContain(store.StoreId.ToString(), p, StringComparison.OrdinalIgnoreCase));
        Assert.All(payloads, p => Assert.DoesNotContain(store.TenantId.ToString(), p, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Consumer_endpoints_answer_anonymous_callers_without_coverage()
    {
        using var anonymous = factory.CreateClientUnredirected();
        const double latitude = -54.8000d;
        const double longitude = -68.3000d;

        var catalog = await ConsumerTestData.SearchAsync(anonymous, latitude, longitude);
        Assert.False(catalog.ServiceAvailable);
        Assert.Empty(catalog.Items);
        Assert.Equal(0, catalog.TotalCount);
        Assert.Equal(1, catalog.Page);
        Assert.Equal(20, catalog.PageSize);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ConsumerTestData.ProductAsync(anonymous, Guid.CreateVersion7(), latitude, longitude)).StatusCode);
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Reserving the whole balance is the one case where stock exists but Available is zero.
    /// There is no reservation endpoint yet, so the balance is moved directly.
    /// </summary>
    private async Task ReserveEverythingAsync(Guid storeId, Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var item = await db.InventoryItems.SingleAsync(i => i.StoreId == storeId && i.GlobalProductId == productId);

        var property = typeof(InventoryItem).GetProperty(
            nameof(InventoryItem.Reserved),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(item, item.OnHand);
        await db.SaveChangesAsync();
    }
}
