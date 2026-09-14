using System.Net;
using System.Net.Http.Json;
using Chevrere.Infrastructure.Audit;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Inventory.Domain;
using Chevrere.Modules.Procurement.Application.Abstractions;
using Chevrere.Modules.Procurement.Application.Contracts;
using Chevrere.Modules.Procurement.Application.Idempotency;
using Chevrere.Modules.Procurement.Domain;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Idempotency;
using Chevrere.SharedKernel.Inventory;
using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.IntegrationTests;

/// <summary>
/// Two requests carrying the same Idempotency-Key must produce one purchase order, one goods
/// receipt and one inventory movement, and both callers must see the same response.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProcurementConcurrencyTests(ChevrereApiFactory factory)
{
    private readonly ProcurementTests _seed = new(factory);

    [Fact]
    public async Task Concurrent_same_key_create_purchase_order_creates_one_order()
    {
        var ctx = await _seed.SeedAsync("PUCC1");
        using var ownerA = await _seed.OwnerClientAsync(ctx.Email);
        using var ownerB = await _seed.OwnerClientAsync(ctx.Email);
        var supplier = await ProcurementTests.CreateSupplierAsync(ownerA, "PUCC1");
        var body = new CreatePurchaseOrderRequest(
            supplier.Id, "Concurrente", [ProcurementTests.Line(ctx.ProductId, 10, 1000)]);
        const string key = "pcc1-po-0000001";
        var url = ProcurementTests.PurchaseOrdersUrl(ctx);

        var responses = await Task.WhenAll(
            ProcurementTests.PostMutationAsync(ownerA, url, body, key),
            ProcurementTests.PostMutationAsync(ownerB, url, body, key));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var a = await responses[0].Content.ReadFromJsonAsync<PurchaseOrderDto>(AuthHelper.Json);
        var b = await responses[1].Content.ReadFromJsonAsync<PurchaseOrderDto>(AuthHelper.Json);
        Assert.Equal(a!.Id, b!.Id);
        Assert.Equal(a.Number, b.Number);
        Assert.Equal(1, await _seed.CountPurchaseOrdersAsync(ctx.StoreId));
        Assert.Equal(1, await _seed.CountAuditsAsync(AuditActions.PurchaseOrderCreated, ctx.TenantId));
        Assert.Equal(1, await CountIdempotencyAsync(
            ctx.TenantId, IdempotencyOperations.ProcurementPurchaseOrderCreate, key));
    }

    [Fact]
    public async Task Concurrent_same_key_receive_moves_stock_once()
    {
        var ctx = await _seed.SeedAsync("PUCC2");
        using var ownerA = await _seed.OwnerClientAsync(ctx.Email);
        using var ownerB = await _seed.OwnerClientAsync(ctx.Email);
        var order = await ProcurementTests.ApprovedOrderAsync(ownerA, ctx, "PUCC2", (ctx.ProductId, 10, 1000));
        var body = new ReceiveGoodsRequest(
            "Concurrente", [new ReceiveGoodsLineRequest(order.Items[0].Id, 6)]);
        const string key = "pcc2-rcv-000001";
        var url = ProcurementTests.ReceiptsUrl(ctx, order.Id);

        var responses = await Task.WhenAll(
            ProcurementTests.PostMutationAsync(ownerA, url, body, key),
            ProcurementTests.PostMutationAsync(ownerB, url, body, key));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var a = await responses[0].Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json);
        var b = await responses[1].Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json);
        Assert.Equal(a!.Id, b!.Id);
        Assert.Equal(a.ReceiptNumber, b.ReceiptNumber);
        Assert.Equal(a.Items[0].InventoryMovementId, b.Items[0].InventoryMovementId);
        Assert.NotNull(a.Items[0].InventoryMovementId);

        Assert.Equal(6, await _seed.OnHandAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await _seed.CountReceiptsAsync(order.Id));
        Assert.Equal(1, await _seed.CountMovementsAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await _seed.CountAuditsAsync(AuditActions.GoodsReceiptRecorded, ctx.TenantId));
        Assert.Equal(1, await CountIdempotencyAsync(
            ctx.TenantId, IdempotencyOperations.ProcurementReceive, key));
        Assert.Equal(1, await _seed.CountReceiptMovementsAsync(a.Items[0].Id));
    }

    [Fact]
    public async Task Concurrent_same_key_different_payload_rejects_the_loser()
    {
        var ctx = await _seed.SeedAsync("PUCC3");
        using var ownerA = await _seed.OwnerClientAsync(ctx.Email);
        using var ownerB = await _seed.OwnerClientAsync(ctx.Email);
        var order = await ProcurementTests.ApprovedOrderAsync(ownerA, ctx, "PUCC3", (ctx.ProductId, 10, 1000));
        var lineId = order.Items[0].Id;
        const string key = "pcc3-rcv-000001";
        var url = ProcurementTests.ReceiptsUrl(ctx, order.Id);

        var responses = await Task.WhenAll(
            ProcurementTests.PostMutationAsync(
                ownerA, url, new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(lineId, 4)]), key),
            ProcurementTests.PostMutationAsync(
                ownerB, url, new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(lineId, 5)]), key));

        var ok = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        var conflict = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();
        Assert.Single(ok);
        Assert.Single(conflict);
        var problem = await conflict[0].Content.ReadFromJsonAsync<ProblemDetails>(AuthHelper.Json);
        Assert.Equal("idempotency.key.reused", problem!.Title);

        var winner = await ok[0].Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json);
        Assert.NotNull(winner);
        Assert.True(winner.Items[0].ReceivedQuantity is 4 or 5);
        Assert.Equal(winner.Items[0].ReceivedQuantity, await _seed.OnHandAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await _seed.CountReceiptsAsync(order.Id));
        Assert.Equal(1, await _seed.CountMovementsAsync(ctx.StoreId, ctx.ProductId));
    }

    /// <summary>
    /// Two receipts racing with different keys both hold the same purchase order: xmin lets only one
    /// commit, and the loser must not leave a receipt or a stock movement behind.
    /// </summary>
    [Fact]
    public async Task Concurrent_different_keys_receive_do_not_both_apply()
    {
        var ctx = await _seed.SeedAsync("PUCC4");
        using var ownerA = await _seed.OwnerClientAsync(ctx.Email);
        using var ownerB = await _seed.OwnerClientAsync(ctx.Email);
        var order = await ProcurementTests.ApprovedOrderAsync(ownerA, ctx, "PUCC4", (ctx.ProductId, 10, 1000));
        var body = new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(order.Items[0].Id, 7)]);
        var url = ProcurementTests.ReceiptsUrl(ctx, order.Id);

        var responses = await Task.WhenAll(
            ProcurementTests.PostMutationAsync(ownerA, url, body, "pcc4-rcv-a00001"),
            ProcurementTests.PostMutationAsync(ownerB, url, body, "pcc4-rcv-b00001"));

        var statuses = responses.Select(r => r.StatusCode).ToArray();
        Assert.True(
            statuses.Count(s => s == HttpStatusCode.OK) == 1,
            $"Expected exactly one success, got: {string.Join(',', statuses)}");
        Assert.Contains(HttpStatusCode.Conflict, statuses);

        Assert.Equal(7, await _seed.OnHandAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await _seed.CountReceiptsAsync(order.Id));
        Assert.Equal(1, await _seed.CountMovementsAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await _seed.CountAuditsAsync(AuditActions.GoodsReceiptRecorded, ctx.TenantId));

        var reread = await ownerA.GetFromJsonAsync<PurchaseOrderDto>(
            $"{ProcurementTests.PurchaseOrdersUrl(ctx)}/{order.Id}", AuthHelper.Json);
        Assert.Equal(7, reread!.Items[0].ReceivedQuantity);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, reread.Status);
    }

    /// <summary>
    /// A replay must answer with the snapshot the caller originally saw, even though a later receipt
    /// already moved the purchase order to Received.
    /// </summary>
    [Fact]
    public async Task Replay_returns_the_original_snapshot_after_a_second_receipt()
    {
        var ctx = await _seed.SeedAsync("PUCC5");
        using var owner = await _seed.OwnerClientAsync(ctx.Email);
        var order = await ProcurementTests.ApprovedOrderAsync(owner, ctx, "PUCC5", (ctx.ProductId, 10, 1000));
        var lineId = order.Items[0].Id;
        var firstBody = new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(lineId, 7)]);
        const string firstKey = "pcc5-rcv-000001";
        var url = ProcurementTests.ReceiptsUrl(ctx, order.Id);

        var first = await ProcurementTests.PostMutationAsync(owner, url, firstBody, firstKey);
        first.EnsureSuccessStatusCode();
        var original = await first.Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, original!.PurchaseOrderStatusAfter);

        var second = await ProcurementTests.PostMutationAsync(
            owner,
            url,
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(lineId, 3)]),
            "pcc5-rcv-000002");
        second.EnsureSuccessStatusCode();
        Assert.Equal(
            PurchaseOrderStatus.Received,
            (await second.Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json))!.PurchaseOrderStatusAfter);
        Assert.Equal(10, await _seed.OnHandAsync(ctx.StoreId, ctx.ProductId));

        var replay = await ProcurementTests.PostMutationAsync(owner, url, firstBody, firstKey);
        replay.EnsureSuccessStatusCode();
        var replayed = await replay.Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json);
        Assert.NotNull(replayed);
        Assert.Equal(original.Id, replayed.Id);
        Assert.Equal(original.ReceiptNumber, replayed.ReceiptNumber);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, replayed.PurchaseOrderStatusAfter);
        Assert.Equal(7, replayed.Items[0].ReceivedQuantity);
        Assert.Equal(0, replayed.Items[0].ReceivedBefore);
        Assert.Equal(7, replayed.Items[0].ReceivedAfter);
        Assert.Equal(3, replayed.Items[0].RemainingAfter);
        Assert.Equal(original.Items[0].InventoryMovementId, replayed.Items[0].InventoryMovementId);

        Assert.Equal(10, await _seed.OnHandAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(2, await _seed.CountReceiptsAsync(order.Id));
        Assert.Equal(2, await _seed.CountMovementsAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(2, await _seed.CountAuditsAsync(AuditActions.GoodsReceiptRecorded, ctx.TenantId));
    }

    /// <summary>
    /// White-box check of the loser path: everything the failed receipt staged (purchase order
    /// quantities, the receipt with its lines, the inventory writes, the idempotency row and the
    /// audit event) is detached from its own DbContext, while unrelated pending work in the same
    /// change tracker survives and still commits.
    /// </summary>
    [Fact]
    public async Task Loser_same_key_receive_recovery_cleans_same_dbcontext_without_global_detach()
    {
        var ctx = await _seed.SeedAsync("PUCC6");
        using var owner = await _seed.OwnerClientAsync(ctx.Email);
        var order = await ProcurementTests.ApprovedOrderAsync(owner, ctx, "PUCC6", (ctx.ProductId, 10, 1000));
        var lineId = order.Items[0].Id;
        const string key = "pcc6-rcv-000001";
        var lines = new[] { new ReceiveGoodsLineRequest(lineId, 4) };
        var hash = ProcurementFingerprints.ForReceive(ctx.StoreId, order.Id, null, lines);

        var winner = await RecordReceiptAsync(ctx, order.Id, lines, key, hash);

        await using var loserScope = factory.Services.CreateAsyncScope();
        loserScope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var loserDb = loserScope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        var store = loserScope.ServiceProvider.GetRequiredService<IProcurementStore>();
        var numbers = loserScope.ServiceProvider.GetRequiredService<IDocumentNumberGenerator>();
        var inventory = loserScope.ServiceProvider.GetRequiredService<IInventoryInboundService>();
        var idempotency = loserScope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var audit = loserScope.ServiceProvider.GetRequiredService<IAuditRecorder>();
        var unitOfWork = loserScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = loserScope.ServiceProvider.GetRequiredService<IClock>();

        var keptCategory = await loserDb.Categories.SingleAsync(c => c.Id == ctx.CategoryId);
        keptCategory.Update("Categoría PCC6 Kept", null, 3, clock.UtcNow);
        Assert.Equal(EntityState.Modified, loserDb.Entry(keptCategory).State);

        var attempt = await StageReceiptAsync(
            store, numbers, inventory, idempotency, audit, clock, ctx, order.Id, lines, key, hash);
        var receiptLine = attempt.Receipt.Items[0];
        var orderLine = attempt.Order.Items[0];

        Assert.Equal(EntityState.Modified, loserDb.Entry(attempt.Order).State);
        Assert.Equal(EntityState.Added, loserDb.Entry(attempt.Receipt).State);
        Assert.Equal(EntityState.Added, loserDb.Entry(receiptLine).State);
        Assert.Equal(EntityState.Modified, loserDb.Entry(orderLine).State);
        Assert.Equal(EntityState.Added, loserDb.Entry(attempt.Idempotency).State);
        Assert.Equal(1, loserDb.ChangeTracker.Entries<AuditEvent>().Count(e => e.State == EntityState.Added));
        Assert.Equal(1, loserDb.ChangeTracker.Entries<InventoryMovement>().Count(e => e.State == EntityState.Added));

        await Assert.ThrowsAnyAsync<Exception>(() => unitOfWork.SaveChangesAsync());

        var replay = await ProcurementIdempotencyReplay.TryReplayGoodsReceiptAsync(
            store, idempotency, audit, inventory, attempt, ctx.TenantId, key, hash, CancellationToken.None);

        Assert.NotNull(replay);
        Assert.True(replay.IsSuccess);
        Assert.Equal(winner.Id, replay.Value.Id);
        Assert.Equal(winner.ReceiptNumber, replay.Value.ReceiptNumber);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, replay.Value.PurchaseOrderStatusAfter);
        Assert.Equal(4, replay.Value.Items[0].ReceivedAfter);
        Assert.Equal(6, replay.Value.Items[0].RemainingAfter);

        Assert.Equal(EntityState.Detached, loserDb.Entry(attempt.Receipt).State);
        Assert.Equal(EntityState.Detached, loserDb.Entry(receiptLine).State);
        Assert.Equal(EntityState.Detached, loserDb.Entry(orderLine).State);
        Assert.Equal(EntityState.Detached, loserDb.Entry(attempt.Idempotency).State);
        Assert.Equal(EntityState.Detached, loserDb.Entry(attempt.Order).State);
        Assert.Equal(0, loserDb.ChangeTracker.Entries<AuditEvent>().Count(e => e.State == EntityState.Added));
        Assert.Equal(0, loserDb.ChangeTracker.Entries<InventoryMovement>().Count(e => e.State == EntityState.Added));
        Assert.Equal(EntityState.Modified, loserDb.Entry(keptCategory).State);
        Assert.DoesNotContain(
            loserDb.ChangeTracker.Entries(),
            e => e.State is EntityState.Added or EntityState.Deleted
                 || (e.State == EntityState.Modified
                     && e.Entity is PurchaseOrder or PurchaseOrderItem or GoodsReceipt or GoodsReceiptItem
                         or InventoryItem or InventoryMovement or IdempotentOperation or AuditEvent));

        await unitOfWork.SaveChangesAsync();

        Assert.Equal("Categoría PCC6 Kept", await loserDb.Categories
            .Where(c => c.Id == keptCategory.Id)
            .Select(c => c.Name)
            .SingleAsync());
        Assert.Equal(4, await _seed.OnHandAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await _seed.CountReceiptsAsync(order.Id));
        Assert.Equal(1, await _seed.CountMovementsAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await _seed.CountAuditsAsync(AuditActions.GoodsReceiptRecorded, ctx.TenantId));
        Assert.Equal(1, await CountIdempotencyAsync(
            ctx.TenantId, IdempotencyOperations.ProcurementReceive, key));
        Assert.Equal(0, await CountAuditsForEntityAsync(
            AuditActions.GoodsReceiptRecorded, attempt.Receipt.Id));
    }

    private async Task<GoodsReceipt> RecordReceiptAsync(
        ProcurementContext ctx,
        Guid purchaseOrderId,
        IReadOnlyList<ReceiveGoodsLineRequest> lines,
        string key,
        string hash)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var attempt = await StageReceiptAsync(
            scope.ServiceProvider.GetRequiredService<IProcurementStore>(),
            scope.ServiceProvider.GetRequiredService<IDocumentNumberGenerator>(),
            scope.ServiceProvider.GetRequiredService<IInventoryInboundService>(),
            scope.ServiceProvider.GetRequiredService<IIdempotencyStore>(),
            scope.ServiceProvider.GetRequiredService<IAuditRecorder>(),
            scope.ServiceProvider.GetRequiredService<IClock>(),
            ctx,
            purchaseOrderId,
            lines,
            key,
            hash);

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return attempt.Receipt;
    }

    /// <summary>
    /// Stages the same writes ReceiveGoodsHandler does, without committing them.
    /// </summary>
    private static async Task<GoodsReceiptAttempt> StageReceiptAsync(
        IProcurementStore store,
        IDocumentNumberGenerator numbers,
        IInventoryInboundService inventory,
        IIdempotencyStore idempotency,
        IAuditRecorder audit,
        IClock clock,
        ProcurementContext ctx,
        Guid purchaseOrderId,
        IReadOnlyList<ReceiveGoodsLineRequest> lines,
        string key,
        string hash)
    {
        var order = await store.GetPurchaseOrderAsync(purchaseOrderId, CancellationToken.None);
        Assert.NotNull(order);

        var receipt = order.Receive(
            [.. lines.Select(l => new GoodsReceiptLine(l.PurchaseOrderItemId, l.Quantity))],
            clock.UtcNow);
        var document = GoodsReceipt.Record(
            order,
            await numbers.NextGoodsReceiptNumberAsync(ctx.TenantId, CancellationToken.None),
            receipt,
            null,
            ctx.OwnerUserId,
            Guid.CreateVersion7().ToString("D"),
            clock.UtcNow);
        store.AddGoodsReceipt(document);

        var inbound = await inventory.ApplyGoodsReceiptAsync(
            new InventoryInboundRequest(
                ctx.TenantId,
                ctx.StoreId,
                ctx.OwnerUserId,
                document.CorrelationId,
                [.. document.Items.Select(i => new InventoryInboundLine(i.GlobalProductId, i.Id, i.ReceivedQuantity))]),
            CancellationToken.None);
        foreach (var line in inbound)
        {
            document.LinkInventoryMovement(line.GoodsReceiptItemId, line.MovementId);
        }

        var operation = new IdempotentOperation
        {
            Id = Guid.CreateVersion7(),
            TenantId = ctx.TenantId,
            Operation = IdempotencyOperations.ProcurementReceive,
            IdempotencyKey = key,
            RequestHash = hash,
            ResourceId = document.Id,
            CreatedAt = clock.UtcNow
        };
        idempotency.Add(operation);

        audit.Record(
            AuditActions.GoodsReceiptRecorded,
            nameof(GoodsReceipt),
            document.Id,
            ctx.TenantId,
            previousValue: new { PurchaseOrderStatus = receipt.StatusBefore },
            newValue: new { document.ReceiptNumber, PurchaseOrderStatus = receipt.StatusAfter });

        return new GoodsReceiptAttempt(order, document, inbound, operation);
    }

    private async Task<int> CountAuditsForEntityAsync(string action, Guid entityId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.Set<AuditEvent>().CountAsync(e => e.Action == action && e.EntityId == entityId);
    }

    private async Task<int> CountIdempotencyAsync(Guid tenantId, string operation, string key)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.Set<IdempotentOperation>().CountAsync(
            o => o.TenantId == tenantId && o.Operation == operation && o.IdempotencyKey == key);
    }
}
