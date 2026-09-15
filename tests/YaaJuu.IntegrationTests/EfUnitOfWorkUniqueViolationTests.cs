using System.Net.Http.Json;
using YaaJuu.Infrastructure.Audit;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Catalog.Application.Abstractions;
using YaaJuu.Modules.Catalog.Application.Contracts;
using YaaJuu.Modules.Catalog.Domain;
using YaaJuu.Modules.Catalog.Domain.ValueObjects;
using YaaJuu.Modules.Tenancy.Application.Contracts;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class EfUnitOfWorkUniqueViolationTests(YaaJuuApiFactory factory)
{
    [Fact]
    public async Task Unique_violation_does_not_detach_unrelated_tracked_changes()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var categoryDto = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-UOW", "Bebidas UoW");
        var existing = await CatalogAdminTests.CreateProductAsync(
            admin,
            categoryDto.Id,
            "SKU-UOW-EXIST",
            "Producto existente",
            "7707777777771");

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var kept = await db.Categories.SingleAsync(c => c.Id == categoryDto.Id);
        kept.Update("Bebidas UoW Modified", "kept-change", 1, clock.UtcNow);
        Assert.Equal(EntityState.Modified, db.Entry(kept).State);

        var duplicate = GlobalProduct.Create(
            kept,
            Sku.Create(existing.Sku),
            "Duplicado",
            Slug.FromName($"dup-uow-{Guid.NewGuid():N}"),
            "Marca",
            "1 L",
            null,
            null,
            clock.UtcNow);
        db.GlobalProducts.Add(duplicate);
        Assert.Equal(EntityState.Added, db.Entry(duplicate).State);

        await Assert.ThrowsAsync<DuplicateKeyException>(() => uow.SaveChangesAsync());

        Assert.Equal(EntityState.Modified, db.Entry(kept).State);
        Assert.Equal(EntityState.Added, db.Entry(duplicate).State);
    }

    [Fact]
    public async Task Failed_store_product_enable_discards_only_loser_and_its_audit()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var categoryDto = await CatalogAdminTests.CreateCategoryAsync(admin, "BEB-RCV", "Bebidas Recovery");
        var productDto = await CatalogAdminTests.CreateProductAsync(
            admin,
            categoryDto.Id,
            "SKU-RCV-1",
            "Producto Recovery",
            "7707777777772");
        var franchisee = await CreateFranchiseeAsync(admin, "RCV1", "owner-rcv1@example.com", "918777001");

        await using var winnerScope = factory.Services.CreateAsyncScope();
        winnerScope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var winnerDb = winnerScope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var winnerClock = winnerScope.ServiceProvider.GetRequiredService<IClock>();
        var product = await winnerDb.GlobalProducts.SingleAsync(p => p.Id == productDto.Id);
        var winner = StoreProduct.EnableForStore(franchisee.TenantId, franchisee.StoreId, product, winnerClock.UtcNow);
        winnerDb.StoreProducts.Add(winner);
        winnerDb.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.CreateVersion7(),
            TenantId = franchisee.TenantId,
            ActorUserId = null,
            Action = AuditActions.StoreProductEnabled,
            EntityType = nameof(StoreProduct),
            EntityId = winner.Id,
            OccurredAt = winnerClock.UtcNow,
            CorrelationId = Guid.CreateVersion7().ToString("D"),
            PreviousValue = null,
            NewValue = """{"isEnabled":true}"""
        });
        await winnerDb.SaveChangesAsync();

        await using var loserScope = factory.Services.CreateAsyncScope();
        loserScope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var loserDb = loserScope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var store = loserScope.ServiceProvider.GetRequiredService<ICatalogStore>();
        var audit = loserScope.ServiceProvider.GetRequiredService<IAuditRecorder>();
        var uow = loserScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = loserScope.ServiceProvider.GetRequiredService<IClock>();

        var kept = await loserDb.Categories.SingleAsync(c => c.Id == categoryDto.Id);
        kept.Update("Bebidas Recovery Kept", null, 2, clock.UtcNow);

        var productAgain = await loserDb.GlobalProducts.SingleAsync(p => p.Id == productDto.Id);
        var loser = StoreProduct.EnableForStore(franchisee.TenantId, franchisee.StoreId, productAgain, clock.UtcNow);
        store.AddStoreProduct(loser);
        audit.Record(
            AuditActions.StoreProductEnabled,
            nameof(StoreProduct),
            loser.Id,
            franchisee.TenantId,
            previousValue: null,
            newValue: new { loser.StoreId, loser.GlobalProductId, IsEnabled = true });

        await Assert.ThrowsAsync<DuplicateKeyException>(() => uow.SaveChangesAsync());

        Assert.Equal(EntityState.Modified, loserDb.Entry(kept).State);
        Assert.Equal(EntityState.Added, loserDb.Entry(loser).State);
        Assert.Equal(1, loserDb.ChangeTracker.Entries<AuditEvent>().Count(e => e.State == EntityState.Added));

        store.DiscardTracked(loser);
        audit.DiscardPending(AuditActions.StoreProductEnabled, nameof(StoreProduct), loser.Id);

        Assert.Equal(EntityState.Detached, loserDb.Entry(loser).State);
        Assert.Equal(0, loserDb.ChangeTracker.Entries<AuditEvent>().Count(e => e.State == EntityState.Added));
        Assert.Equal(EntityState.Modified, loserDb.Entry(kept).State);

        await uow.SaveChangesAsync();

        Assert.Equal(1, await loserDb.StoreProducts.CountAsync(
            s => s.StoreId == franchisee.StoreId && s.GlobalProductId == productDto.Id));
        Assert.Equal(1, await loserDb.Set<AuditEvent>().CountAsync(
            e => e.Action == AuditActions.StoreProductEnabled && e.EntityId == winner.Id));
        Assert.Equal(0, await loserDb.Set<AuditEvent>().CountAsync(
            e => e.Action == AuditActions.StoreProductEnabled && e.EntityId == loser.Id));
        Assert.Equal("Bebidas Recovery Kept", await loserDb.Categories
            .Where(c => c.Id == categoryDto.Id)
            .Select(c => c.Name)
            .SingleAsync());
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
}
