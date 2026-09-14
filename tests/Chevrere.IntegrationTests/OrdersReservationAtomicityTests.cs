using System.Net;
using System.Net.Http.Json;
using Chevrere.Infrastructure.Audit;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Inventory.Domain;
using Chevrere.Modules.Orders.Application.Commands;
using Chevrere.Modules.Orders.Application.Contracts;
using Chevrere.Modules.Orders.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Inventory;
using Chevrere.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class OrdersReservationAtomicityTests(ChevrereApiFactory factory)
{
    [Fact]
    public async Task Release_of_a_missing_reservation_leaves_tracked_aggregates_untouched()
    {
        var ctx = await SeedThreeLineOrderAsync("ORA1", "930702001", 4.5100d, -74.5100d);
        var missing = ctx.Items[1];
        await DeleteReservationAsync(missing.Id);

        await using var scope = Scope();
        var db = Db(scope);
        var service = Reservations(scope);
        var refs = ctx.Items.Select(i => i.Id).ToList();
        var before = await SnapshotAsync(db, ctx);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.ReleaseAsync(ReleaseRequest(ctx, refs), CancellationToken.None));

        Assert.Equal(InventoryReservationErrors.IncompleteSet, ex.Code);
        AssertNoTrackedRelease(db);
        AssertActiveTrackedReservations(db);
        await AssertUnchangedAsync(db, ctx, before);
        await db.SaveChangesAsync();
        await AssertPersistedUnchangedAsync(ctx, before);
    }

    [Fact]
    public async Task Commit_of_a_missing_reservation_leaves_on_hand_and_reserved_untouched()
    {
        var ctx = await SeedThreeLineOrderAsync("ORA2", "930702002", 4.5200d, -74.5200d);
        await DeleteReservationAsync(ctx.Items[1].Id);

        await using var scope = Scope();
        var db = Db(scope);
        var service = Reservations(scope);
        var before = await SnapshotAsync(db, ctx);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.CommitAsync(ReleaseRequest(ctx, [.. ctx.Items.Select(i => i.Id)]), CancellationToken.None));

        Assert.Equal(InventoryReservationErrors.IncompleteSet, ex.Code);
        Assert.Equal(0, db.ChangeTracker.Entries<InventoryMovement>().Count(e => e.State == EntityState.Added));
        AssertActiveTrackedReservations(db);
        await AssertUnchangedAsync(db, ctx, before);
        await db.SaveChangesAsync();
        await AssertPersistedUnchangedAsync(ctx, before);
    }

    [Fact]
    public async Task Release_rejects_a_committed_sibling_before_mutating_active_lines()
    {
        var ctx = await SeedThreeLineOrderAsync("ORA3", "930702003", 4.5300d, -74.5300d);
        await using (var prep = Scope())
        {
            await Reservations(prep).CommitAsync(
                ReleaseRequest(ctx, [ctx.Items[1].Id]), CancellationToken.None);
            await Db(prep).SaveChangesAsync();
        }

        await using var scope = Scope();
        var db = Db(scope);
        var before = await SnapshotAsync(db, ctx);
        var ex = await Assert.ThrowsAsync<DomainException>(
            () => Reservations(scope).ReleaseAsync(
                ReleaseRequest(ctx, [.. ctx.Items.Select(i => i.Id)]), CancellationToken.None));

        Assert.Equal(InventoryReservationErrors.InvalidState, ex.Code);
        Assert.Equal(0, db.ChangeTracker.Entries<InventoryMovement>().Count(e => e.State == EntityState.Added));
        var tracked = db.ChangeTracker.Entries<InventoryReservation>().Select(e => e.Entity).ToList();
        Assert.Contains(tracked, r => r.ReferenceId == ctx.Items[0].Id && r.Status == InventoryReservationStatus.Active);
        Assert.Contains(tracked, r => r.ReferenceId == ctx.Items[2].Id && r.Status == InventoryReservationStatus.Active);
        Assert.Contains(tracked, r => r.ReferenceId == ctx.Items[1].Id && r.Status == InventoryReservationStatus.Committed);
        await db.SaveChangesAsync();
        await AssertPersistedUnchangedAsync(ctx, before);
    }

    [Fact]
    public async Task Commit_rejects_a_released_sibling_before_mutating_active_lines()
    {
        var ctx = await SeedThreeLineOrderAsync("ORA4", "930702004", 4.5400d, -74.5400d);
        await using (var prep = Scope())
        {
            await Reservations(prep).ReleaseAsync(
                ReleaseRequest(ctx, [ctx.Items[1].Id]), CancellationToken.None);
            await Db(prep).SaveChangesAsync();
        }

        await using var scope = Scope();
        var db = Db(scope);
        var before = await SnapshotAsync(db, ctx);
        var ex = await Assert.ThrowsAsync<DomainException>(
            () => Reservations(scope).CommitAsync(
                ReleaseRequest(ctx, [.. ctx.Items.Select(i => i.Id)]), CancellationToken.None));

        Assert.Equal(InventoryReservationErrors.InvalidState, ex.Code);
        Assert.Equal(0, db.ChangeTracker.Entries<InventoryMovement>().Count(e => e.State == EntityState.Added));
        var tracked = db.ChangeTracker.Entries<InventoryReservation>().Select(e => e.Entity).ToList();
        Assert.Contains(tracked, r => r.ReferenceId == ctx.Items[0].Id && r.Status == InventoryReservationStatus.Active);
        Assert.Contains(tracked, r => r.ReferenceId == ctx.Items[2].Id && r.Status == InventoryReservationStatus.Active);
        Assert.Contains(tracked, r => r.ReferenceId == ctx.Items[1].Id && r.Status == InventoryReservationStatus.Released);
        await db.SaveChangesAsync();
        await AssertPersistedUnchangedAsync(ctx, before);
    }

    [Fact]
    public async Task Release_retry_mix_posts_movements_only_for_active_lines()
    {
        var ctx = await SeedThreeLineOrderAsync("ORA5", "930702005", 4.5500d, -74.5500d);
        await using (var prep = Scope())
        {
            await Reservations(prep).ReleaseAsync(
                ReleaseRequest(ctx, [ctx.Items[1].Id]), CancellationToken.None);
            await Db(prep).SaveChangesAsync();
        }

        await using var scope = Scope();
        await Reservations(scope).ReleaseAsync(
            ReleaseRequest(ctx, [.. ctx.Items.Select(i => i.Id)]), CancellationToken.None);
        var db = Db(scope);
        Assert.Equal(2, db.ChangeTracker.Entries<InventoryMovement>().Count(e =>
            e.State == EntityState.Added && e.Entity.Type == InventoryMovementType.ReservationReleased));
        await db.SaveChangesAsync();

        var statuses = await LoadStatusesAsync(ctx.OrderId);
        Assert.All(statuses, s => Assert.Equal(InventoryReservationStatus.Released, s));
        Assert.Equal(2, await CountNewReleasedAsync(ctx, ctx.Items[1].Id));
        Assert.Equal(0L, await ReservedTotalAsync(ctx));
    }

    [Fact]
    public async Task Commit_retry_mix_posts_movements_only_for_active_lines()
    {
        var ctx = await SeedThreeLineOrderAsync("ORA6", "930702006", 4.5600d, -74.5600d);
        await using (var prep = Scope())
        {
            await Reservations(prep).CommitAsync(
                ReleaseRequest(ctx, [ctx.Items[1].Id]), CancellationToken.None);
            await Db(prep).SaveChangesAsync();
        }

        await using var scope = Scope();
        await Reservations(scope).CommitAsync(
            ReleaseRequest(ctx, [.. ctx.Items.Select(i => i.Id)]), CancellationToken.None);
        var db = Db(scope);
        Assert.Equal(2, db.ChangeTracker.Entries<InventoryMovement>().Count(e =>
            e.State == EntityState.Added && e.Entity.Type == InventoryMovementType.ReservationCommitted));
        await db.SaveChangesAsync();

        var statuses = await LoadStatusesAsync(ctx.OrderId);
        Assert.All(statuses, s => Assert.Equal(InventoryReservationStatus.Committed, s));
        Assert.Equal(2, await CountMovementsAsync(ctx.StoreId, InventoryMovementType.ReservationCommitted) - 1);
        Assert.Equal(0L, await ReservedTotalAsync(ctx));
    }

    [Fact]
    public async Task Cancel_does_not_persist_when_a_reservation_is_missing()
    {
        var ctx = await SeedThreeLineOrderAsync("ORA7", "930702007", 4.5700d, -74.5700d);
        var before = await DurableSnapshotAsync(ctx);
        await DeleteReservationAsync(ctx.Items[1].Id);

        var response = await ctx.Client.PostAsJsonAsync(
            $"/api/v1/consumer/orders/{ctx.OrderId}/cancel", new CancelOrderRequest("nope"));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(AuthHelper.Json);
        Assert.Equal(InventoryReservationErrors.IncompleteSet, problem!.Title);

        await AssertOrderUntouchedAsync(ctx, before, AuditActions.OrderCancelled);
    }

    [Fact]
    public async Task Expire_does_not_persist_or_succeed_when_a_reservation_is_missing()
    {
        var ctx = await SeedThreeLineOrderAsync("ORA8", "930702008", 4.5800d, -74.5800d);
        var before = await DurableSnapshotAsync(ctx);
        await DeleteReservationAsync(ctx.Items[1].Id);

        await using var scope = Scope();
        scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set(Guid.CreateVersion7().ToString("D"));
        var handler = scope.ServiceProvider.GetRequiredService<IHandler<ExpireOrderCommand, Result>>();
        var result = await handler.HandleAsync(new ExpireOrderCommand(ctx.OrderId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryReservationErrors.IncompleteSet, result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        await AssertOrderUntouchedAsync(ctx, before, AuditActions.OrderExpired);
    }

    private async Task<SeededOrder> SeedThreeLineOrderAsync(
        string suffix, string identification, double lat, double lon)
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedThreeProductStoreAsync(
            factory, admin, suffix, identification, lat, lon);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(
            factory, $"cons-{suffix.ToLowerInvariant()}@example.com", $"Cons {suffix}");
        await OrdersTestData.PutItemBodyAsync(consumer, seed.ProductIds[0], seed.Latitude, seed.Longitude, 2);
        await OrdersTestData.PutItemBodyAsync(consumer, seed.ProductIds[1], seed.Latitude, seed.Longitude, 1);
        await OrdersTestData.PutItemBodyAsync(consumer, seed.ProductIds[2], seed.Latitude, seed.Longitude, 3);
        var order = await OrdersTestData.CreateOrderBodyAsync(
            consumer, seed.Latitude, seed.Longitude, $"{suffix.ToLowerInvariant()}-ord-00001");

        await using var scope = Scope();
        var rows = await Db(scope).OrderItems.IgnoreQueryFilters()
            .Where(i => i.OrderId == order.Id)
            .ToListAsync();
        var items = seed.ProductIds
            .Select(productId => rows.Single(i => i.GlobalProductId == productId))
            .Select(i => new Line(i.Id, i.GlobalProductId, i.Quantity))
            .ToList();
        Assert.Equal(3, items.Count);
        return new SeededOrder(seed.Store.TenantId, seed.Store.StoreId, order.Id, items, consumer, seed.ProductIds);
    }

    private async Task DeleteReservationAsync(Guid referenceId)
    {
        await using var scope = Scope();
        var deleted = await Db(scope).InventoryReservations.IgnoreQueryFilters()
            .Where(r => r.ReferenceId == referenceId)
            .ExecuteDeleteAsync();
        Assert.Equal(1, deleted);
    }

    private AsyncServiceScope Scope()
    {
        var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set(Guid.CreateVersion7().ToString("D"));
        return scope;
    }

    private static ChevrereDbContext Db(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();

    private static IInventoryReservationService Reservations(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IInventoryReservationService>();

    private static InventoryReservationReleaseRequest ReleaseRequest(SeededOrder ctx, IReadOnlyList<Guid> refs) =>
        new(ctx.TenantId, ctx.StoreId, null, Guid.CreateVersion7().ToString("D"), InventoryReferenceTypes.OrderItem, refs);

    private static void AssertNoTrackedRelease(ChevrereDbContext db)
    {
        Assert.Equal(0, db.ChangeTracker.Entries<InventoryMovement>().Count(e => e.State == EntityState.Added));
        Assert.DoesNotContain(
            db.ChangeTracker.Entries<InventoryReservation>(),
            e => e.Entity.Status != InventoryReservationStatus.Active);
        Assert.DoesNotContain(
            db.ChangeTracker.Entries<InventoryItem>(),
            e => e.State == EntityState.Modified);
    }

    private static void AssertActiveTrackedReservations(ChevrereDbContext db)
    {
        var tracked = db.ChangeTracker.Entries<InventoryReservation>().Select(e => e.Entity).ToList();
        Assert.Equal(2, tracked.Count);
        Assert.All(tracked, r => Assert.Equal(InventoryReservationStatus.Active, r.Status));
        Assert.All(tracked, r => Assert.Equal(EntityState.Unchanged, db.Entry(r).State));
    }

    private static async Task<BalanceSnapshot> SnapshotAsync(ChevrereDbContext db, SeededOrder ctx)
    {
        var productIds = ctx.ProductIds.ToList();
        var items = await db.InventoryItems.IgnoreQueryFilters()
            .Where(i => i.StoreId == ctx.StoreId && productIds.Contains(i.GlobalProductId))
            .OrderBy(i => i.GlobalProductId)
            .Select(i => new ItemBalance(i.GlobalProductId, i.OnHand, i.Reserved))
            .ToListAsync();
        var released = await db.InventoryMovements.IgnoreQueryFilters().CountAsync(m =>
            m.StoreId == ctx.StoreId && m.Type == InventoryMovementType.ReservationReleased);
        var committed = await db.InventoryMovements.IgnoreQueryFilters().CountAsync(m =>
            m.StoreId == ctx.StoreId && m.Type == InventoryMovementType.ReservationCommitted);
        return new BalanceSnapshot(items, released, committed);
    }

    private async Task<BalanceSnapshot> DurableSnapshotAsync(SeededOrder ctx)
    {
        await using var scope = Scope();
        return await SnapshotAsync(Db(scope), ctx);
    }

    private async Task AssertPersistedUnchangedAsync(SeededOrder ctx, BalanceSnapshot before)
    {
        await using var scope = Scope();
        var latest = await SnapshotAsync(Db(scope), ctx);
        Assert.Equal(before.ReleasedMovements, latest.ReleasedMovements);
        Assert.Equal(before.CommittedMovements, latest.CommittedMovements);
        Assert.Equal(before.Items, latest.Items);
    }

    private static async Task AssertUnchangedAsync(ChevrereDbContext db, SeededOrder ctx, BalanceSnapshot before)
    {
        foreach (var item in before.Items)
        {
            var current = await db.InventoryItems.IgnoreQueryFilters()
                .SingleAsync(i => i.StoreId == ctx.StoreId && i.GlobalProductId == item.ProductId);
            Assert.Equal(item.OnHand, current.OnHand);
            Assert.Equal(item.Reserved, current.Reserved);
        }
    }

    private async Task AssertOrderUntouchedAsync(SeededOrder ctx, BalanceSnapshot before, string auditAction)
    {
        await using var scope = Scope();
        var db = Db(scope);
        var status = await db.Orders.IgnoreQueryFilters()
            .Where(o => o.Id == ctx.OrderId)
            .Select(o => o.Status)
            .SingleAsync();
        Assert.Equal(OrderStatus.PendingPayment, status);
        Assert.Equal(0, await db.AuditEvents.IgnoreQueryFilters().CountAsync(e => e.EntityId == ctx.OrderId && e.Action == auditAction));
        var latest = await SnapshotAsync(db, ctx);
        Assert.Equal(before.ReleasedMovements, latest.ReleasedMovements);
        Assert.Equal(before.CommittedMovements, latest.CommittedMovements);
        Assert.Equal(before.Items, latest.Items);
        var remaining = await db.InventoryReservations.IgnoreQueryFilters()
            .CountAsync(r => r.StoreId == ctx.StoreId && r.Status == InventoryReservationStatus.Active);
        Assert.Equal(2, remaining);
    }

    private async Task<IReadOnlyList<InventoryReservationStatus>> LoadStatusesAsync(Guid orderId)
    {
        await using var scope = Scope();
        var db = Db(scope);
        var itemIds = await db.OrderItems.IgnoreQueryFilters()
            .Where(i => i.OrderId == orderId)
            .Select(i => i.Id)
            .ToListAsync();
        return await db.InventoryReservations.IgnoreQueryFilters()
            .Where(r => itemIds.Contains(r.ReferenceId))
            .Select(r => r.Status)
            .ToListAsync();
    }

    private async Task<int> CountNewReleasedAsync(SeededOrder ctx, Guid alreadyReleasedReferenceId)
    {
        await using var scope = Scope();
        var db = Db(scope);
        var releasedReservationId = await db.InventoryReservations.IgnoreQueryFilters()
            .Where(r => r.ReferenceId == alreadyReleasedReferenceId)
            .Select(r => r.Id)
            .SingleAsync();
        var total = await db.InventoryMovements.IgnoreQueryFilters().CountAsync(m =>
            m.StoreId == ctx.StoreId && m.Type == InventoryMovementType.ReservationReleased);
        var forAlreadyReleased = await db.InventoryMovements.IgnoreQueryFilters().CountAsync(m =>
            m.Type == InventoryMovementType.ReservationReleased && m.ReferenceId == releasedReservationId);
        Assert.Equal(1, forAlreadyReleased);
        return total - forAlreadyReleased;
    }

    private async Task<int> CountMovementsAsync(Guid storeId, InventoryMovementType type)
    {
        await using var scope = Scope();
        return await Db(scope).InventoryMovements.IgnoreQueryFilters()
            .CountAsync(m => m.StoreId == storeId && m.Type == type);
    }

    private async Task<long> ReservedTotalAsync(SeededOrder ctx)
    {
        await using var scope = Scope();
        var productIds = ctx.ProductIds.ToList();
        return await Db(scope).InventoryItems.IgnoreQueryFilters()
            .Where(i => i.StoreId == ctx.StoreId && productIds.Contains(i.GlobalProductId))
            .SumAsync(i => i.Reserved);
    }

    private sealed record SeededOrder(
        Guid TenantId,
        Guid StoreId,
        Guid OrderId,
        IReadOnlyList<Line> Items,
        HttpClient Client,
        IReadOnlyList<Guid> ProductIds);

    private sealed record Line(Guid Id, Guid ProductId, long Quantity);

    private sealed record ItemBalance(Guid ProductId, long OnHand, long Reserved);

    private sealed record BalanceSnapshot(IReadOnlyList<ItemBalance> Items, int ReleasedMovements, int CommittedMovements);
}
