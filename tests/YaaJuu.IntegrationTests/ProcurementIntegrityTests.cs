using System.Net.Http.Json;
using System.Reflection;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Catalog.Application.Contracts;
using YaaJuu.Modules.Procurement.Application.Contracts;
using YaaJuu.Modules.Procurement.Domain;
using YaaJuu.SharedKernel.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.IntegrationTests;

/// <summary>
/// PostgreSQL must reject a goods-receipt line that points at another purchase order's item
/// (same tenant) or that lies about the product, even when Domain/Application are bypassed.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProcurementIntegrityTests(YaaJuuApiFactory factory)
{
    private readonly ProcurementTests _seed = new(factory);

    [Fact]
    public async Task Database_rejects_receipt_item_from_another_purchase_order()
    {
        var ctx = await _seed.SeedAsync("PUXPO");
        using var owner = await _seed.OwnerClientAsync(ctx.Email);

        var orderA = await ProcurementTests.ApprovedOrderAsync(
            owner, ctx, "PUXPOA", (ctx.ProductId, 10, 1000));
        var orderB = await ProcurementTests.ApprovedOrderAsync(
            owner, ctx, "PUXPOB", (ctx.ProductId, 10, 1000));

        var receiptResponse = await ProcurementTests.PostMutationAsync(
            owner,
            ProcurementTests.ReceiptsUrl(ctx, orderA.Id),
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(orderA.Items[0].Id, 3)]),
            "puxpo-rcv-000001");
        receiptResponse.EnsureSuccessStatusCode();
        var receipt = (await receiptResponse.Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json))!;

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();

        var rogue = BypassGoodsReceiptItem(
            goodsReceiptId: receipt.Id,
            tenantId: ctx.TenantId,
            purchaseOrderId: orderA.Id,
            purchaseOrderItemId: orderB.Items[0].Id,
            globalProductId: ctx.ProductId,
            receivedQuantity: 1,
            receivedBefore: 0,
            receivedAfter: 1,
            remainingAfter: 9);

        db.GoodsReceiptItems.Add(rogue);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.NotNull(ex.InnerException);
        Assert.Contains(
            "fk_goods_receipt_items_purchase_order_items_purchase_order_ite",
            ex.InnerException.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Database_rejects_receipt_item_with_wrong_product()
    {
        var ctx = await _seed.SeedAsync("PUPRD");
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var otherProduct = await CatalogAdminTests.CreateProductAsync(
            admin, ctx.CategoryId, "SKU-PUPRD-B", "Producto PUPRD B", null);
        using var owner = await _seed.OwnerClientAsync(ctx.Email);
        (await owner.PostAsync(
            $"/api/v1/business/stores/{ctx.StoreId}/products/{otherProduct.Id}/enable", null))
            .EnsureSuccessStatusCode();

        var order = await ProcurementTests.ApprovedOrderAsync(
            owner, ctx, "PUPRD", (ctx.ProductId, 10, 1000));
        var receiptResponse = await ProcurementTests.PostMutationAsync(
            owner,
            ProcurementTests.ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(order.Items[0].Id, 2)]),
            "puprd-rcv-000001");
        receiptResponse.EnsureSuccessStatusCode();
        var receipt = (await receiptResponse.Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json))!;

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();

        var line = await db.GoodsReceiptItems.SingleAsync(i => i.Id == receipt.Items[0].Id);
        Assert.Equal(ctx.ProductId, line.GlobalProductId);
        Set(line, nameof(GoodsReceiptItem.GlobalProductId), otherProduct.Id);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.NotNull(ex.InnerException);
        Assert.Contains(
            "fk_goods_receipt_items_purchase_order_items_purchase_order_ite",
            ex.InnerException.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Database_accepts_receipt_item_aligned_with_purchase_order()
    {
        var ctx = await _seed.SeedAsync("PUOK1");
        using var owner = await _seed.OwnerClientAsync(ctx.Email);
        var orderDto = await ProcurementTests.ApprovedOrderAsync(
            owner, ctx, "PUOK1", (ctx.ProductId, 10, 1000));

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();

        var order = await db.PurchaseOrders
            .Include(o => o.Items)
            .SingleAsync(o => o.Id == orderDto.Id);

        var receipt = order.Receive([new GoodsReceiptLine(order.Items[0].Id, 5)], DateTimeOffset.UtcNow);
        var document = GoodsReceipt.Record(
            order, "GR-INTEGRITY-OK", receipt, null, null, "corr-integrity-ok", DateTimeOffset.UtcNow);

        db.GoodsReceipts.Add(document);
        await db.SaveChangesAsync();

        var persisted = await db.GoodsReceiptItems.AsNoTracking()
            .SingleAsync(i => i.GoodsReceiptId == document.Id);
        Assert.Equal(order.Id, persisted.PurchaseOrderId);
        Assert.Equal(order.Items[0].Id, persisted.PurchaseOrderItemId);
        Assert.Equal(ctx.ProductId, persisted.GlobalProductId);
        Assert.Equal(5, persisted.ReceivedQuantity);
    }

    private static GoodsReceiptItem BypassGoodsReceiptItem(
        Guid goodsReceiptId,
        Guid tenantId,
        Guid purchaseOrderId,
        Guid purchaseOrderItemId,
        Guid globalProductId,
        long receivedQuantity,
        long receivedBefore,
        long receivedAfter,
        long remainingAfter)
    {
        var item = (GoodsReceiptItem)Activator.CreateInstance(typeof(GoodsReceiptItem), nonPublic: true)!;
        var now = DateTimeOffset.UtcNow;
        Set(item, nameof(GoodsReceiptItem.Id), Guid.CreateVersion7());
        Set(item, nameof(GoodsReceiptItem.GoodsReceiptId), goodsReceiptId);
        Set(item, nameof(GoodsReceiptItem.TenantId), tenantId);
        Set(item, nameof(GoodsReceiptItem.PurchaseOrderId), purchaseOrderId);
        Set(item, nameof(GoodsReceiptItem.PurchaseOrderItemId), purchaseOrderItemId);
        Set(item, nameof(GoodsReceiptItem.GlobalProductId), globalProductId);
        Set(item, nameof(GoodsReceiptItem.ReceivedQuantity), receivedQuantity);
        Set(item, nameof(GoodsReceiptItem.ReceivedBefore), receivedBefore);
        Set(item, nameof(GoodsReceiptItem.ReceivedAfter), receivedAfter);
        Set(item, nameof(GoodsReceiptItem.RemainingAfter), remainingAfter);
        Set(item, nameof(GoodsReceiptItem.CreatedAt), now);
        Set(item, nameof(GoodsReceiptItem.UpdatedAt), now);
        return item;
    }

    private static void Set(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(
            propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(target, value);
    }
}
