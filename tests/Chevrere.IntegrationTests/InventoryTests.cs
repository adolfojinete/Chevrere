using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Chevrere.Infrastructure.Audit;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Inventory.Application.Contracts;
using Chevrere.Modules.Inventory.Domain;
using Chevrere.Modules.Tenancy.Application.Contracts;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class InventoryTests(ChevrereApiFactory factory)
{
    [Fact]
    public async Task Inventory_ledger_flow_adjust_waste_idempotency_and_history()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-INV", "Bebidas Inventory");
        var product = await CatalogAdminTests.CreateProductAsync(
            admin, category.Id, "SKU-INV-FLOW", "Coca Inventory Flow", "7708888000001");
        var franchisee = await CreateFranchiseeAsync(admin, "INV1", "owner-inv1@example.com", "919777001");

        using var owner = await OwnerClientAsync("owner-inv1@example.com");
        (await owner.PostAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/enable", null))
            .EnsureSuccessStatusCode();

        var listBefore = await owner.GetFromJsonAsync<PagedResultDto<InventoryItemDto>>(
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory", AuthHelper.Json);
        Assert.NotNull(listBefore);
        var pending = Assert.Single(listBefore.Items, i => i.GlobalProductId == product.Id);
        Assert.False(pending.InventoryInitialized);
        Assert.Equal(0, pending.OnHand);

        var detailPending = await owner.GetFromJsonAsync<InventoryItemDto>(
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory/{product.Id}", AuthHelper.Json);
        Assert.NotNull(detailPending);
        Assert.False(detailPending.InventoryInitialized);

        var init = await PostMutationAsync(
            owner,
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory/{product.Id}/initialize",
            new InitializeInventoryRequest(50),
            "init-key-0001");
        Assert.Equal(HttpStatusCode.OK, init.StatusCode);
        var initBody = await init.Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        Assert.NotNull(initBody);
        Assert.Equal(50, initBody.OnHand);
        Assert.Equal(0, initBody.Reserved);
        Assert.Equal(50, initBody.Available);
        Assert.NotNull(initBody.MovementId);

        var initReplay = await PostMutationAsync(
            owner,
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory/{product.Id}/initialize",
            new InitializeInventoryRequest(50),
            "init-key-0001");
        Assert.Equal(HttpStatusCode.OK, initReplay.StatusCode);
        Assert.Equal(1, await CountItemsAsync(franchisee.StoreId, product.Id));
        Assert.Equal(1, await CountMovementsAsync(franchisee.StoreId, product.Id));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.InventoryInitialized, franchisee.TenantId));

        var initMismatch = await PostMutationAsync(
            owner,
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory/{product.Id}/initialize",
            new InitializeInventoryRequest(30),
            "init-key-0001");
        Assert.Equal(HttpStatusCode.Conflict, initMismatch.StatusCode);
        await AssertProblem(initMismatch, "idempotency.key.reused");

        var reinit = await PostMutationAsync(
            owner,
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory/{product.Id}/initialize",
            new InitializeInventoryRequest(10),
            "init-key-other");
        Assert.Equal(HttpStatusCode.Conflict, reinit.StatusCode);
        await AssertProblem(reinit, "inventory.already_initialized");

        var increase = await PostMutationAsync(
            owner,
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory/{product.Id}/adjustments",
            new AdjustInventoryRequest(InventoryAdjustmentType.Increase, 10, "Conteo físico"),
            "adj-key-0001");
        increase.EnsureSuccessStatusCode();
        var afterIncrease = await increase.Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        Assert.NotNull(afterIncrease);
        Assert.Equal(60, afterIncrease.OnHand);

        var increaseReplay = await PostMutationAsync(
            owner,
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory/{product.Id}/adjustments",
            new AdjustInventoryRequest(InventoryAdjustmentType.Increase, 10, "Conteo físico"),
            "adj-key-0001");
        increaseReplay.EnsureSuccessStatusCode();
        Assert.Equal(60, await GetOnHandAsync(franchisee.StoreId, product.Id));
        Assert.Equal(2, await CountMovementsAsync(franchisee.StoreId, product.Id));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.InventoryAdjusted, franchisee.TenantId));

        var increaseMismatch = await PostMutationAsync(
            owner,
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory/{product.Id}/adjustments",
            new AdjustInventoryRequest(InventoryAdjustmentType.Increase, 20, "Conteo físico"),
            "adj-key-0001");
        Assert.Equal(HttpStatusCode.Conflict, increaseMismatch.StatusCode);
        await AssertProblem(increaseMismatch, "idempotency.key.reused");

        var decrease = await PostMutationAsync(
            owner,
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory/{product.Id}/adjustments",
            new AdjustInventoryRequest(InventoryAdjustmentType.Decrease, 15, "Corrección inventario"),
            "adj-key-0002");
        decrease.EnsureSuccessStatusCode();
        Assert.Equal(45, (await decrease.Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json))!.OnHand);

        var waste = await PostMutationAsync(
            owner,
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory/{product.Id}/waste",
            new WasteInventoryRequest(5, "Producto roto"),
            "waste-key-0001");
        waste.EnsureSuccessStatusCode();
        Assert.Equal(40, (await waste.Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json))!.OnHand);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.InventoryWasteRecorded, franchisee.TenantId));

        var wasteReplay = await PostMutationAsync(
            owner,
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory/{product.Id}/waste",
            new WasteInventoryRequest(5, "Producto roto"),
            "waste-key-0001");
        wasteReplay.EnsureSuccessStatusCode();
        Assert.Equal(40, await GetOnHandAsync(franchisee.StoreId, product.Id));
        Assert.Equal(4, await CountMovementsAsync(franchisee.StoreId, product.Id));

        var insufficient = await PostMutationAsync(
            owner,
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory/{product.Id}/adjustments",
            new AdjustInventoryRequest(InventoryAdjustmentType.Decrease, 50, "Exceso"),
            "adj-key-fail1");
        Assert.Equal(HttpStatusCode.Conflict, insufficient.StatusCode);
        await AssertProblem(insufficient, "inventory.insufficient_available");
        Assert.Equal(40, await GetOnHandAsync(franchisee.StoreId, product.Id));
        Assert.Equal(4, await CountMovementsAsync(franchisee.StoreId, product.Id));
        Assert.Null(await FindIdempotencyAsync(franchisee.TenantId, IdempotencyOperations.InventoryAdjust, "adj-key-fail1"));

        var movements = await owner.GetFromJsonAsync<PagedResultDto<InventoryMovementDto>>(
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory/{product.Id}/movements",
            AuthHelper.Json);
        Assert.NotNull(movements);
        Assert.Equal(4, movements.TotalCount);
        Assert.Equal(
            new[] { -5L, -15L, 10L, 50L },
            movements.Items.Select(m => m.OnHandDelta).ToArray());
        Assert.All(movements.Items, m =>
        {
            Assert.Equal(0, m.ReservedBefore);
            Assert.Equal(0, m.ReservedAfter);
            Assert.Equal(0, m.ReservedDelta);
        });

        Assert.Equal(0, movements.Items[0].OnHandAfter - movements.Items[0].OnHandBefore - movements.Items[0].OnHandDelta);
        Assert.Equal(45, movements.Items[0].OnHandBefore);
        Assert.Equal(40, movements.Items[0].OnHandAfter);
        Assert.Equal(60, movements.Items[1].OnHandBefore);
        Assert.Equal(45, movements.Items[1].OnHandAfter);
        Assert.Equal(50, movements.Items[2].OnHandBefore);
        Assert.Equal(60, movements.Items[2].OnHandAfter);
        Assert.Equal(0, movements.Items[3].OnHandBefore);
        Assert.Equal(50, movements.Items[3].OnHandAfter);

        var adminList = await admin.GetFromJsonAsync<PagedResultDto<InventoryItemDto>>(
            $"/api/v1/admin/stores/{franchisee.StoreId}/inventory", AuthHelper.Json);
        Assert.NotNull(adminList);
        Assert.Contains(adminList.Items, i => i.GlobalProductId == product.Id && i.OnHand == 40);
    }

    [Fact]
    public async Task Initialize_zero_has_no_movement_and_adjust_requires_initialization()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-INV0", "Bebidas Inv0");
        var product = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-INV0", "Zero Stock", null);
        var franchisee = await CreateFranchiseeAsync(admin, "INV0", "owner-inv0@example.com", "919777002");
        using var owner = await OwnerClientAsync("owner-inv0@example.com");
        (await owner.PostAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/enable", null))
            .EnsureSuccessStatusCode();

        var beforeInit = await PostMutationAsync(
            owner,
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory/{product.Id}/adjustments",
            new AdjustInventoryRequest(InventoryAdjustmentType.Increase, 1, "Premature"),
            "adj-before-init");
        Assert.Equal(HttpStatusCode.Conflict, beforeInit.StatusCode);
        await AssertProblem(beforeInit, "inventory.not_initialized");

        var init0 = await PostMutationAsync(
            owner,
            $"/api/v1/business/stores/{franchisee.StoreId}/inventory/{product.Id}/initialize",
            new InitializeInventoryRequest(0),
            "init-zero-0001");
        init0.EnsureSuccessStatusCode();
        var body = await init0.Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        Assert.NotNull(body);
        Assert.Null(body.MovementId);
        Assert.Equal(0, body.OnHand);
        Assert.Equal(0, await CountMovementsAsync(franchisee.StoreId, product.Id));
        Assert.Equal(1, await CountItemsAsync(franchisee.StoreId, product.Id));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.InventoryInitialized, franchisee.TenantId));
    }

    [Fact]
    public async Task Owner_cannot_access_foreign_store_inventory_and_missing_offering_conflicts()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-IISO", "Bebidas Inv Iso");
        var offered = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-IISO-1", "Offered", null);
        var notOffered = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-IISO-2", "Not Offered", null);
        var a = await CreateFranchiseeAsync(admin, "IISOA", "owner-iisoa@example.com", "919777003");
        var b = await CreateFranchiseeAsync(admin, "IISOB", "owner-iisob@example.com", "919777004");

        using var ownerA = await OwnerClientAsync("owner-iisoa@example.com");
        (await ownerA.PostAsync($"/api/v1/business/stores/{a.StoreId}/products/{offered.Id}/enable", null))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await ownerA.GetAsync(
            $"/api/v1/business/stores/{b.StoreId}/inventory")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await PostMutationAsync(
            ownerA,
            $"/api/v1/business/stores/{b.StoreId}/inventory/{offered.Id}/initialize",
            new InitializeInventoryRequest(1),
            "foreign-init-1")).StatusCode);

        var notAssociated = await PostMutationAsync(
            ownerA,
            $"/api/v1/business/stores/{a.StoreId}/inventory/{notOffered.Id}/initialize",
            new InitializeInventoryRequest(1),
            "not-offered-1");
        Assert.Equal(HttpStatusCode.Conflict, notAssociated.StatusCode);
        await AssertProblem(notAssociated, "inventory.product.not_offered");

        var missingKey = await ownerA.PostAsJsonAsync(
            $"/api/v1/business/stores/{a.StoreId}/inventory/{offered.Id}/initialize",
            new InitializeInventoryRequest(1));
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
    }

    [Fact]
    public async Task Database_rejects_negative_reserved_overflow_cross_tenant_and_duplicates()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-IDB", "Bebidas Inv Db");
        var product = await CatalogAdminTests.CreateProductAsync(admin, category.Id, "SKU-IDB-1", "Db Inv", null);
        var a = await CreateFranchiseeAsync(admin, "IDBA", "owner-idba@example.com", "919777005");
        var b = await CreateFranchiseeAsync(admin, "IDBB", "owner-idbb@example.com", "919777006");

        using var ownerA = await OwnerClientAsync("owner-idba@example.com");
        (await ownerA.PostAsync($"/api/v1/business/stores/{a.StoreId}/products/{product.Id}/enable", null))
            .EnsureSuccessStatusCode();
        (await PostMutationAsync(
            ownerA,
            $"/api/v1/business/stores/{a.StoreId}/inventory/{product.Id}/initialize",
            new InitializeInventoryRequest(10),
            "db-init-0001")).EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();

        var item = await db.InventoryItems.SingleAsync(i => i.StoreId == a.StoreId && i.GlobalProductId == product.Id);
        Set(item, nameof(InventoryItem.OnHand), -1L);
        var negativeEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("ck_inventory_items_on_hand_non_negative", negativeEx.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();

        item = await db.InventoryItems.SingleAsync(i => i.StoreId == a.StoreId && i.GlobalProductId == product.Id);
        Set(item, nameof(InventoryItem.Reserved), 11L);
        var reservedEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("ck_inventory_items_reserved_lte_on_hand", reservedEx.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();

        var rogue = BypassItem(a.TenantId, b.StoreId, product.Id, 1);
        db.InventoryItems.Add(rogue);
        var fkEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("foreign key", fkEx.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();

        var withoutOffering = BypassItem(b.TenantId, b.StoreId, product.Id, 1);
        db.InventoryItems.Add(withoutOffering);
        var offeringEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("foreign key", offeringEx.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();

        var duplicate = BypassItem(a.TenantId, a.StoreId, product.Id, 3);
        db.InventoryItems.Add(duplicate);
        var uniqueEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("ix_inventory_items_tenant_store_product", uniqueEx.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpClient> OwnerClientAsync(string email)
    {
        var client = factory.CreateClientUnredirected();
        var token = await AuthHelper.LoginAsync(client, email, "OwnerTest!23456");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
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

    private static async Task<HttpResponseMessage> PostMutationAsync<T>(
        HttpClient client,
        string url,
        T body,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, AuthHelper.Json), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task AssertProblem(HttpResponseMessage response, string code)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(AuthHelper.Json);
        Assert.NotNull(problem);
        Assert.Equal(code, problem.Title);
    }

    private async Task<long> GetOnHandAsync(Guid storeId, Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.InventoryItems.Where(i => i.StoreId == storeId && i.GlobalProductId == productId)
            .Select(i => i.OnHand)
            .SingleAsync();
    }

    private async Task<int> CountItemsAsync(Guid storeId, Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.InventoryItems.CountAsync(i => i.StoreId == storeId && i.GlobalProductId == productId);
    }

    private async Task<int> CountMovementsAsync(Guid storeId, Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.InventoryMovements.CountAsync(m => m.StoreId == storeId && m.GlobalProductId == productId);
    }

    private async Task<int> CountAuditsAsync(string action, Guid tenantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.Set<AuditEvent>().CountAsync(e => e.Action == action && e.TenantId == tenantId);
    }

    private async Task<IdempotentOperation?> FindIdempotencyAsync(Guid tenantId, string operation, string key)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.Set<IdempotentOperation>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Operation == operation && o.IdempotencyKey == key);
    }

    private static InventoryItem BypassItem(Guid tenantId, Guid storeId, Guid productId, long onHand)
    {
        var item = (InventoryItem)Activator.CreateInstance(typeof(InventoryItem), nonPublic: true)!;
        Set(item, nameof(InventoryItem.Id), Guid.CreateVersion7());
        Set(item, nameof(InventoryItem.TenantId), tenantId);
        Set(item, nameof(InventoryItem.StoreId), storeId);
        Set(item, nameof(InventoryItem.GlobalProductId), productId);
        Set(item, nameof(InventoryItem.OnHand), onHand);
        Set(item, nameof(InventoryItem.Reserved), 0L);
        Set(item, nameof(InventoryItem.CreatedAt), DateTimeOffset.UtcNow);
        Set(item, nameof(InventoryItem.UpdatedAt), DateTimeOffset.UtcNow);
        return item;
    }

    private static void Set(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(target, value);
    }
}
