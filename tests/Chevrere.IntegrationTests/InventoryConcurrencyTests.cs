using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Inventory.Application.Contracts;
using Chevrere.Modules.Tenancy.Application.Contracts;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class InventoryConcurrencyTests(ChevrereApiFactory factory)
{
    [Fact]
    public async Task Concurrent_same_key_increase_applies_once_and_replays_winner_movement()
    {
        var ctx = await SeedInitializedAsync("CCINC", 10);
        using var ownerA = await OwnerClientAsync(ctx.Email);
        using var ownerB = await OwnerClientAsync(ctx.Email);

        const string key = "same-key-inc-01";
        var body = new AdjustInventoryRequest(InventoryAdjustmentType.Increase, 5, "Conteo físico");
        var url = AdjustUrl(ctx);

        var responses = await Task.WhenAll(
            PostMutationAsync(ownerA, url, body, key),
            PostMutationAsync(ownerB, url, body, key));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var a = await responses[0].Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        var b = await responses[1].Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a.MovementId, b.MovementId);
        Assert.Equal(15, a.OnHand);
        Assert.Equal(15, b.OnHand);
        Assert.Equal(15, await GetOnHandAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await CountMovementsOfTypeAsync(ctx.StoreId, ctx.ProductId, "AdjustmentIncrease"));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.InventoryAdjusted, ctx.TenantId));
        Assert.Equal(1, await CountIdempotencyAsync(ctx.TenantId, IdempotencyOperations.InventoryAdjust, key));
    }

    [Fact]
    public async Task Concurrent_same_key_waste_applies_once()
    {
        var ctx = await SeedInitializedAsync("CCWST", 10);
        using var ownerA = await OwnerClientAsync(ctx.Email);
        using var ownerB = await OwnerClientAsync(ctx.Email);
        const string key = "same-key-waste1";
        var body = new WasteInventoryRequest(2, "Producto roto");
        var url = WasteUrl(ctx);

        var responses = await Task.WhenAll(
            PostMutationAsync(ownerA, url, body, key),
            PostMutationAsync(ownerB, url, body, key));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var a = await responses[0].Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        var b = await responses[1].Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a.MovementId, b.MovementId);
        Assert.Equal(8, a.OnHand);
        Assert.Equal(8, await GetOnHandAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await CountMovementsOfTypeAsync(ctx.StoreId, ctx.ProductId, "Waste"));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.InventoryWasteRecorded, ctx.TenantId));
        Assert.Equal(1, await CountIdempotencyAsync(ctx.TenantId, IdempotencyOperations.InventoryWaste, key));
    }

    [Fact]
    public async Task Concurrent_same_key_initialize_creates_single_item_and_movement()
    {
        var ctx = await SeedOfferingAsync("CCINI");
        using var ownerA = await OwnerClientAsync(ctx.Email);
        using var ownerB = await OwnerClientAsync(ctx.Email);
        const string key = "same-key-init50";
        var body = new InitializeInventoryRequest(50);
        var url = InitUrl(ctx);

        var responses = await Task.WhenAll(
            PostMutationAsync(ownerA, url, body, key),
            PostMutationAsync(ownerB, url, body, key));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var a = await responses[0].Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        var b = await responses[1].Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a.InventoryItemId, b.InventoryItemId);
        Assert.Equal(a.MovementId, b.MovementId);
        Assert.Equal(50, a.OnHand);
        Assert.Equal(1, await CountItemsAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await CountMovementsAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.InventoryInitialized, ctx.TenantId));
        Assert.Equal(1, await CountIdempotencyAsync(ctx.TenantId, IdempotencyOperations.InventoryInitialize, key));
    }

    [Fact]
    public async Task Concurrent_same_key_initialize_zero_has_no_movement()
    {
        var ctx = await SeedOfferingAsync("CCIN0");
        using var ownerA = await OwnerClientAsync(ctx.Email);
        using var ownerB = await OwnerClientAsync(ctx.Email);
        const string key = "same-key-init00";
        var body = new InitializeInventoryRequest(0);
        var url = InitUrl(ctx);

        var responses = await Task.WhenAll(
            PostMutationAsync(ownerA, url, body, key),
            PostMutationAsync(ownerB, url, body, key));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var a = await responses[0].Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        var b = await responses[1].Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Null(a.MovementId);
        Assert.Null(b.MovementId);
        Assert.Equal(0, a.OnHand);
        Assert.Equal(0, b.OnHand);
        Assert.Equal(1, await CountItemsAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(0, await CountMovementsAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.InventoryInitialized, ctx.TenantId));
    }

    [Fact]
    public async Task Concurrent_different_keys_decrease_do_not_both_apply()
    {
        var ctx = await SeedInitializedAsync("CCDKD", 10);
        using var ownerA = await OwnerClientAsync(ctx.Email);
        using var ownerB = await OwnerClientAsync(ctx.Email);
        var body = new AdjustInventoryRequest(InventoryAdjustmentType.Decrease, 7, "Corrección");
        var url = AdjustUrl(ctx);

        var responses = await Task.WhenAll(
            PostMutationAsync(ownerA, url, body, "diff-key-a-0001"),
            PostMutationAsync(ownerB, url, body, "diff-key-b-0001"));

        var statuses = responses.Select(r => r.StatusCode).OrderBy(s => s).ToArray();
        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.True(
            statuses.Count(s => s == HttpStatusCode.OK) == 1,
            $"Expected exactly one success, got: {string.Join(',', statuses)}");
        Assert.True(
            statuses.Any(s => s is HttpStatusCode.Conflict),
            $"Expected one conflict, got: {string.Join(',', statuses)}");

        var onHand = await GetOnHandAsync(ctx.StoreId, ctx.ProductId);
        Assert.Equal(3, onHand);
        Assert.Equal(1, await CountMovementsOfTypeAsync(ctx.StoreId, ctx.ProductId, "AdjustmentDecrease"));
        Assert.True(onHand >= 0);
    }

    [Fact]
    public async Task Concurrent_same_key_different_payload_rejects_loser()
    {
        var ctx = await SeedInitializedAsync("CCSDK", 10);
        using var ownerA = await OwnerClientAsync(ctx.Email);
        using var ownerB = await OwnerClientAsync(ctx.Email);
        const string key = "same-key-diffpay";
        var url = AdjustUrl(ctx);

        var responses = await Task.WhenAll(
            PostMutationAsync(ownerA, url, new AdjustInventoryRequest(InventoryAdjustmentType.Increase, 5, "Conteo"), key),
            PostMutationAsync(ownerB, url, new AdjustInventoryRequest(InventoryAdjustmentType.Increase, 10, "Conteo"), key));

        var ok = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        var conflict = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();
        Assert.Single(ok);
        Assert.Single(conflict);
        var problem = await conflict[0].Content.ReadFromJsonAsync<ProblemDetails>(AuthHelper.Json);
        Assert.Equal("idempotency.key.reused", problem!.Title);

        var winner = await ok[0].Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        Assert.NotNull(winner);
        Assert.True(winner.OnHand is 15 or 20);
        Assert.Equal(winner.OnHand, await GetOnHandAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await CountMovementsOfTypeAsync(ctx.StoreId, ctx.ProductId, "AdjustmentIncrease"));
        Assert.Equal(1, await CountIdempotencyAsync(ctx.TenantId, IdempotencyOperations.InventoryAdjust, key));
    }

    [Fact]
    public async Task Concurrent_different_keys_initialize_only_one_succeeds()
    {
        var ctx = await SeedOfferingAsync("CCDIN");
        using var ownerA = await OwnerClientAsync(ctx.Email);
        using var ownerB = await OwnerClientAsync(ctx.Email);
        var body = new InitializeInventoryRequest(50);
        var url = InitUrl(ctx);

        var responses = await Task.WhenAll(
            PostMutationAsync(ownerA, url, body, "init-key-a-0001"),
            PostMutationAsync(ownerB, url, body, "init-key-b-0001"));

        var ok = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflict = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();
        Assert.Equal(1, ok);
        Assert.Single(conflict);
        var problem = await conflict[0].Content.ReadFromJsonAsync<ProblemDetails>(AuthHelper.Json);
        Assert.Equal("inventory.already_initialized", problem!.Title);
        Assert.Equal(1, await CountItemsAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await CountMovementsAsync(ctx.StoreId, ctx.ProductId));
    }

    [Fact]
    public async Task Replay_returns_original_snapshot_after_later_mutation()
    {
        var ctx = await SeedInitializedAsync("CCREP", 10);
        using var owner = await OwnerClientAsync(ctx.Email);

        var first = await PostMutationAsync(
            owner,
            AdjustUrl(ctx),
            new AdjustInventoryRequest(InventoryAdjustmentType.Increase, 5, "Conteo físico"),
            "replay-key-abc1");
        first.EnsureSuccessStatusCode();
        var original = await first.Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        Assert.NotNull(original);
        Assert.Equal(15, original.OnHand);

        var later = await PostMutationAsync(
            owner,
            AdjustUrl(ctx),
            new AdjustInventoryRequest(InventoryAdjustmentType.Increase, 2, "Otro ajuste"),
            "replay-key-xyz1");
        later.EnsureSuccessStatusCode();
        Assert.Equal(17, await GetOnHandAsync(ctx.StoreId, ctx.ProductId));

        var replay = await PostMutationAsync(
            owner,
            AdjustUrl(ctx),
            new AdjustInventoryRequest(InventoryAdjustmentType.Increase, 5, "Conteo físico"),
            "replay-key-abc1");
        replay.EnsureSuccessStatusCode();
        var replayed = await replay.Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        Assert.NotNull(replayed);
        Assert.Equal(original.MovementId, replayed.MovementId);
        Assert.Equal(15, replayed.OnHand);
        Assert.Equal(17, await GetOnHandAsync(ctx.StoreId, ctx.ProductId));
    }

    [Fact]
    public async Task Initialize_replay_returns_original_snapshot_after_later_change()
    {
        var ctx = await SeedOfferingAsync("CCIRI");
        using var owner = await OwnerClientAsync(ctx.Email);

        var init = await PostMutationAsync(owner, InitUrl(ctx), new InitializeInventoryRequest(10), "init-replay-10");
        init.EnsureSuccessStatusCode();
        var original = await init.Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        Assert.Equal(10, original!.OnHand);

        (await PostMutationAsync(
            owner,
            AdjustUrl(ctx),
            new AdjustInventoryRequest(InventoryAdjustmentType.Increase, 5, "Después"),
            "after-init-adj1")).EnsureSuccessStatusCode();
        Assert.Equal(15, await GetOnHandAsync(ctx.StoreId, ctx.ProductId));

        var replay = await PostMutationAsync(owner, InitUrl(ctx), new InitializeInventoryRequest(10), "init-replay-10");
        replay.EnsureSuccessStatusCode();
        var replayed = await replay.Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        Assert.NotNull(replayed);
        Assert.Equal(original.MovementId, replayed.MovementId);
        Assert.Equal(10, replayed.OnHand);
    }

    [Fact]
    public async Task Initialize_zero_replay_returns_zeros_after_later_change()
    {
        var ctx = await SeedOfferingAsync("CCIR0");
        using var owner = await OwnerClientAsync(ctx.Email);

        (await PostMutationAsync(owner, InitUrl(ctx), new InitializeInventoryRequest(0), "init-zero-repl")).EnsureSuccessStatusCode();
        (await PostMutationAsync(
            owner,
            AdjustUrl(ctx),
            new AdjustInventoryRequest(InventoryAdjustmentType.Increase, 5, "Después"),
            "after-zero-adj1")).EnsureSuccessStatusCode();
        Assert.Equal(5, await GetOnHandAsync(ctx.StoreId, ctx.ProductId));

        var replay = await PostMutationAsync(owner, InitUrl(ctx), new InitializeInventoryRequest(0), "init-zero-repl");
        replay.EnsureSuccessStatusCode();
        var replayed = await replay.Content.ReadFromJsonAsync<InventoryMutationDto>(AuthHelper.Json);
        Assert.NotNull(replayed);
        Assert.Null(replayed.MovementId);
        Assert.Equal(0, replayed.OnHand);
        Assert.Equal(0, replayed.Reserved);
        Assert.Equal(0, replayed.Available);
    }

    [Fact]
    public async Task Failed_same_key_attempt_does_not_leave_pending_tracker_entries()
    {
        var ctx = await SeedInitializedAsync("CCLEK", 10);
        using var owner = await OwnerClientAsync(ctx.Email);
        const string key = "leak-check-key1";
        var body = new AdjustInventoryRequest(InventoryAdjustmentType.Increase, 5, "Conteo físico");

        using var ownerA = await OwnerClientAsync(ctx.Email);
        using var ownerB = await OwnerClientAsync(ctx.Email);
        (await Task.WhenAll(
            PostMutationAsync(ownerA, AdjustUrl(ctx), body, key),
            PostMutationAsync(ownerB, AdjustUrl(ctx), body, key))).ToList().ForEach(r => r.EnsureSuccessStatusCode());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        Assert.DoesNotContain(
            db.ChangeTracker.Entries(),
            e => e.State is EntityState.Added or EntityState.Modified);

        (await PostMutationAsync(
            owner,
            AdjustUrl(ctx),
            new AdjustInventoryRequest(InventoryAdjustmentType.Increase, 1, "Siguiente"),
            "leak-follow-up1")).EnsureSuccessStatusCode();

        Assert.Equal(16, await GetOnHandAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(2, await CountMovementsOfTypeAsync(ctx.StoreId, ctx.ProductId, "AdjustmentIncrease"));
    }

    private async Task<SeedContext> SeedInitializedAsync(string suffix, long quantity)
    {
        var ctx = await SeedOfferingAsync(suffix);
        using var owner = await OwnerClientAsync(ctx.Email);
        (await PostMutationAsync(
            owner,
            InitUrl(ctx),
            new InitializeInventoryRequest(quantity),
            $"seed-init-{suffix}")).EnsureSuccessStatusCode();
        return ctx;
    }

    private async Task<SeedContext> SeedOfferingAsync(string suffix)
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, $"BEB-{suffix}", $"Bebidas {suffix}");
        var product = await CatalogAdminTests.CreateProductAsync(
            admin, category.Id, $"SKU-{suffix}", $"Producto {suffix}", null);
        var email = $"owner-{suffix.ToLowerInvariant()}@example.com";
        var franchisee = await CreateFranchiseeAsync(admin, suffix, email, IdentificationFor(suffix));
        using var owner = await OwnerClientAsync(email);
        (await owner.PostAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/enable", null))
            .EnsureSuccessStatusCode();
        return new SeedContext(franchisee.TenantId, franchisee.StoreId, product.Id, email);
    }

    private static string IdentificationFor(string suffix) =>
        "917" + Math.Abs(suffix.GetHashCode(StringComparison.Ordinal)).ToString().PadLeft(6, '0')[..6];

    private async Task<HttpClient> OwnerClientAsync(string email)
    {
        var client = factory.CreateClientUnredirected();
        var token = await AuthHelper.LoginAsync(client, email, "OwnerTest!23456");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task<CreateFranchiseeResponse> CreateFranchiseeAsync(
        HttpClient admin, string suffix, string email, string identification)
    {
        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/franchisees",
            AuthHelper.FranchiseeRequest(suffix, email, identification));
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<CreateFranchiseeResponse>(AuthHelper.Json);
        Assert.NotNull(body);
        return body;
    }

    private static string InitUrl(SeedContext ctx) =>
        $"/api/v1/business/stores/{ctx.StoreId}/inventory/{ctx.ProductId}/initialize";

    private static string AdjustUrl(SeedContext ctx) =>
        $"/api/v1/business/stores/{ctx.StoreId}/inventory/{ctx.ProductId}/adjustments";

    private static string WasteUrl(SeedContext ctx) =>
        $"/api/v1/business/stores/{ctx.StoreId}/inventory/{ctx.ProductId}/waste";

    private static async Task<HttpResponseMessage> PostMutationAsync<T>(
        HttpClient client, string url, T body, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, AuthHelper.Json), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
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

    private async Task<int> CountMovementsOfTypeAsync(Guid storeId, Guid productId, string type)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.InventoryMovements.CountAsync(m =>
            m.StoreId == storeId && m.GlobalProductId == productId && m.Type.ToString() == type);
    }

    private async Task<int> CountAuditsAsync(string action, Guid tenantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.Set<Chevrere.Infrastructure.Audit.AuditEvent>()
            .CountAsync(e => e.Action == action && e.TenantId == tenantId);
    }

    private async Task<int> CountIdempotencyAsync(Guid tenantId, string operation, string key)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.Set<IdempotentOperation>()
            .CountAsync(o => o.TenantId == tenantId && o.Operation == operation && o.IdempotencyKey == key);
    }

    private sealed record SeedContext(Guid TenantId, Guid StoreId, Guid ProductId, string Email);
}
