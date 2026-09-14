using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Chevrere.Infrastructure.Audit;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Modules.Inventory.Application.Contracts;
using Chevrere.Modules.Inventory.Domain;
using Chevrere.Modules.Procurement.Application.Contracts;
using Chevrere.Modules.Procurement.Domain;
using Chevrere.Modules.Tenancy.Application.Contracts;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Domain.ValueObjects;
using Chevrere.SharedKernel.Idempotency;
using Chevrere.SharedKernel.Inventory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ProcurementTests(ChevrereApiFactory factory)
{
    private static int _identifications;


    [Fact]
    public async Task Supplier_crud_and_lifecycle()
    {
        var ctx = await SeedAsync("PUSUP");
        using var owner = await OwnerClientAsync(ctx.Email);

        var created = await owner.PostAsJsonAsync(
            "/api/v1/business/suppliers",
            new CreateSupplierRequest(
                "prov-01", "Distribuidora Andina", "900123456-1", "Ana Ruiz",
                "Ventas@Andina.CO", "+573001112233", "Calle 80 # 20-10", "Entrega martes"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var supplier = await created.Content.ReadFromJsonAsync<SupplierDto>(AuthHelper.Json);
        Assert.NotNull(supplier);
        Assert.Equal("PROV-01", supplier.Code);
        Assert.Equal("ventas@andina.co", supplier.Email);
        Assert.True(supplier.IsActive);

        var duplicate = await owner.PostAsJsonAsync(
            "/api/v1/business/suppliers",
            new CreateSupplierRequest("PROV-01", "Otro", null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await AssertProblem(duplicate, "supplier.code.duplicate");

        var invalid = await owner.PostAsJsonAsync(
            "/api/v1/business/suppliers",
            new CreateSupplierRequest("", "", null, null, "not-an-email", null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var updated = await owner.PutAsJsonAsync(
            $"/api/v1/business/suppliers/{supplier.Id}",
            new UpdateSupplierRequest("Andina SAS", null, "Luis Pérez", null, null, null, null));
        updated.EnsureSuccessStatusCode();
        var afterUpdate = await updated.Content.ReadFromJsonAsync<SupplierDto>(AuthHelper.Json);
        Assert.Equal("Andina SAS", afterUpdate!.Name);
        Assert.Equal("Luis Pérez", afterUpdate.ContactName);
        Assert.Null(afterUpdate.Email);
        Assert.Equal("PROV-01", afterUpdate.Code);

        (await owner.PostAsync($"/api/v1/business/suppliers/{supplier.Id}/deactivate", null))
            .EnsureSuccessStatusCode();
        (await owner.PostAsync($"/api/v1/business/suppliers/{supplier.Id}/deactivate", null))
            .EnsureSuccessStatusCode();
        Assert.Equal(1, await CountAuditsAsync(AuditActions.SupplierDeactivated, ctx.TenantId));

        var inactive = await owner.GetFromJsonAsync<SupplierDto>(
            $"/api/v1/business/suppliers/{supplier.Id}", AuthHelper.Json);
        Assert.False(inactive!.IsActive);

        var orderWithInactiveSupplier = await PostMutationAsync(
            owner,
            PurchaseOrdersUrl(ctx),
            new CreatePurchaseOrderRequest(supplier.Id, null, [Line(ctx.ProductId, 5, 1200)]),
            "po-inactive-001");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, orderWithInactiveSupplier.StatusCode);
        await AssertProblem(orderWithInactiveSupplier, "supplier.not_active");

        (await owner.PostAsync($"/api/v1/business/suppliers/{supplier.Id}/activate", null))
            .EnsureSuccessStatusCode();
        (await owner.PostAsync($"/api/v1/business/suppliers/{supplier.Id}/activate", null))
            .EnsureSuccessStatusCode();
        Assert.Equal(1, await CountAuditsAsync(AuditActions.SupplierActivated, ctx.TenantId));

        var list = await owner.GetFromJsonAsync<PagedResultDto<SupplierDto>>(
            "/api/v1/business/suppliers?search=andina&isActive=true", AuthHelper.Json);
        Assert.NotNull(list);
        Assert.Single(list.Items, s => s.Id == supplier.Id);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.SupplierCreated, ctx.TenantId));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.SupplierUpdated, ctx.TenantId));
    }

    [Fact]
    public async Task Purchase_order_draft_lifecycle_and_state_machine()
    {
        var ctx = await SeedAsync("PUPO1");
        using var owner = await OwnerClientAsync(ctx.Email);
        var supplier = await CreateSupplierAsync(owner, "PUPO1");
        var other = await CreateSupplierAsync(owner, "PUPO1B");

        var created = await PostMutationAsync(
            owner,
            PurchaseOrdersUrl(ctx),
            new CreatePurchaseOrderRequest(supplier.Id, "Pedido inicial", [Line(ctx.ProductId, 10, 1200)]),
            "po-life-000001");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = await created.Content.ReadFromJsonAsync<PurchaseOrderDto>(AuthHelper.Json);
        Assert.NotNull(order);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        Assert.StartsWith("PO-", order.Number, StringComparison.Ordinal);
        Assert.Equal("Pedido inicial", order.Notes);
        var item = Assert.Single(order.Items);
        Assert.Equal(10, item.OrderedQuantity);
        Assert.Equal(0, item.ReceivedQuantity);
        Assert.Equal(10, item.RemainingQuantity);
        Assert.Equal(1200m, item.UnitCostAmount);
        Assert.Equal("COP", item.UnitCostCurrency);

        var updated = await owner.PutAsJsonAsync(
            $"{PurchaseOrdersUrl(ctx)}/{order.Id}",
            new UpdatePurchaseOrderRequest(other.Id, "Pedido corregido", [Line(ctx.ProductId, 20, 1500)]));
        updated.EnsureSuccessStatusCode();
        var afterUpdate = await updated.Content.ReadFromJsonAsync<PurchaseOrderDto>(AuthHelper.Json);
        Assert.Equal(other.Id, afterUpdate!.SupplierId);
        Assert.Equal(20, afterUpdate.Items[0].OrderedQuantity);
        Assert.Equal(1500m, afterUpdate.Items[0].UnitCostAmount);

        var badCurrency = await owner.PutAsJsonAsync(
            $"{PurchaseOrdersUrl(ctx)}/{order.Id}",
            new UpdatePurchaseOrderRequest(
                other.Id,
                null,
                [new PurchaseOrderItemRequest(ctx.ProductId, 1, 1000, "USD")]));
        Assert.Equal(HttpStatusCode.BadRequest, badCurrency.StatusCode);

        var approved = await owner.PostAsync($"{PurchaseOrdersUrl(ctx)}/{order.Id}/approve", null);
        approved.EnsureSuccessStatusCode();
        Assert.Equal(
            PurchaseOrderStatus.Approved,
            (await approved.Content.ReadFromJsonAsync<PurchaseOrderDto>(AuthHelper.Json))!.Status);

        var reapproved = await owner.PostAsync($"{PurchaseOrdersUrl(ctx)}/{order.Id}/approve", null);
        reapproved.EnsureSuccessStatusCode();
        Assert.Equal(1, await CountAuditsAsync(AuditActions.PurchaseOrderApproved, ctx.TenantId));

        var lateEdit = await owner.PutAsJsonAsync(
            $"{PurchaseOrdersUrl(ctx)}/{order.Id}",
            new UpdatePurchaseOrderRequest(other.Id, null, [Line(ctx.ProductId, 1, 1000)]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, lateEdit.StatusCode);
        await AssertProblem(lateEdit, "purchase_order.not_draft");

        var cancelled = await owner.PostAsJsonAsync(
            $"{PurchaseOrdersUrl(ctx)}/{order.Id}/cancel",
            new CancelPurchaseOrderRequest("Proveedor sin stock"));
        cancelled.EnsureSuccessStatusCode();
        var afterCancel = await cancelled.Content.ReadFromJsonAsync<PurchaseOrderDto>(AuthHelper.Json);
        Assert.Equal(PurchaseOrderStatus.Cancelled, afterCancel!.Status);
        Assert.Equal("Proveedor sin stock", afterCancel.CancelReason);
        Assert.NotNull(afterCancel.CancelledAt);

        var recancelled = await owner.PostAsJsonAsync(
            $"{PurchaseOrdersUrl(ctx)}/{order.Id}/cancel",
            new CancelPurchaseOrderRequest("Otra razón"));
        recancelled.EnsureSuccessStatusCode();
        Assert.Equal(1, await CountAuditsAsync(AuditActions.PurchaseOrderCancelled, ctx.TenantId));

        var receiveCancelled = await PostMutationAsync(
            owner,
            $"{PurchaseOrdersUrl(ctx)}/{order.Id}/receipts",
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(item.Id, 1)]),
            "receive-cancel01");
        Assert.Equal(HttpStatusCode.Conflict, receiveCancelled.StatusCode);
        await AssertProblem(receiveCancelled, "purchase_order.not_receivable");

        var byStatus = await owner.GetFromJsonAsync<PagedResultDto<PurchaseOrderSummaryDto>>(
            $"{PurchaseOrdersUrl(ctx)}?status=Cancelled", AuthHelper.Json);
        Assert.NotNull(byStatus);
        var summary = Assert.Single(byStatus.Items, o => o.Id == order.Id);
        Assert.Equal(1, summary.LineCount);
        Assert.Equal(20, summary.OrderedQuantity);
        Assert.Equal(0, summary.ReceivedQuantity);

        var bySupplier = await owner.GetFromJsonAsync<PagedResultDto<PurchaseOrderSummaryDto>>(
            $"{PurchaseOrdersUrl(ctx)}?supplierId={supplier.Id}", AuthHelper.Json);
        Assert.Empty(bySupplier!.Items);
    }

    [Fact]
    public async Task Purchase_order_requires_offered_and_active_products()
    {
        var ctx = await SeedAsync("PUPO2");
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var owner = await OwnerClientAsync(ctx.Email);
        var supplier = await CreateSupplierAsync(owner, "PUPO2");

        var notOffered = await CatalogAdminTests.CreateProductAsync(
            admin, ctx.CategoryId, "SKU-PO2-OFF", "No ofrecido PO2", null);
        var conflict = await PostMutationAsync(
            owner,
            PurchaseOrdersUrl(ctx),
            new CreatePurchaseOrderRequest(supplier.Id, null, [Line(notOffered.Id, 1, 1000)]),
            "po-notoffered01");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await AssertProblem(conflict, "procurement.product.not_offered");

        var deactivated = await CatalogAdminTests.CreateProductAsync(
            admin, ctx.CategoryId, "SKU-PO2-INA", "Inactivo PO2", null);
        (await owner.PostAsync(
            $"/api/v1/business/stores/{ctx.StoreId}/products/{deactivated.Id}/enable", null))
            .EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/v1/admin/products/{deactivated.Id}/deactivate", null))
            .EnsureSuccessStatusCode();

        var inactive = await PostMutationAsync(
            owner,
            PurchaseOrdersUrl(ctx),
            new CreatePurchaseOrderRequest(supplier.Id, null, [Line(deactivated.Id, 1, 1000)]),
            "po-notactive001");
        Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);
        await AssertProblem(inactive, "procurement.product.not_active");

        var unknownSupplier = await PostMutationAsync(
            owner,
            PurchaseOrdersUrl(ctx),
            new CreatePurchaseOrderRequest(Guid.CreateVersion7(), null, [Line(ctx.ProductId, 1, 1000)]),
            "po-nosupplier01");
        Assert.Equal(HttpStatusCode.NotFound, unknownSupplier.StatusCode);

        var missingKey = await owner.PostAsJsonAsync(
            PurchaseOrdersUrl(ctx),
            new CreatePurchaseOrderRequest(supplier.Id, null, [Line(ctx.ProductId, 1, 1000)]));
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
    }

    [Fact]
    public async Task Create_purchase_order_is_idempotent_per_key()
    {
        var ctx = await SeedAsync("PUPOI");
        using var owner = await OwnerClientAsync(ctx.Email);
        var supplier = await CreateSupplierAsync(owner, "PUPOI");
        var body = new CreatePurchaseOrderRequest(supplier.Id, "Semanal", [Line(ctx.ProductId, 8, 900)]);
        const string key = "po-idem-000001";

        var first = await PostMutationAsync(owner, PurchaseOrdersUrl(ctx), body, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var original = await first.Content.ReadFromJsonAsync<PurchaseOrderDto>(AuthHelper.Json);

        var replay = await PostMutationAsync(owner, PurchaseOrdersUrl(ctx), body, key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var replayed = await replay.Content.ReadFromJsonAsync<PurchaseOrderDto>(AuthHelper.Json);
        Assert.Equal(original!.Id, replayed!.Id);
        Assert.Equal(original.Number, replayed.Number);
        Assert.Equal(original.Items[0].Id, replayed.Items[0].Id);

        var mismatch = await PostMutationAsync(
            owner,
            PurchaseOrdersUrl(ctx),
            new CreatePurchaseOrderRequest(supplier.Id, "Semanal", [Line(ctx.ProductId, 9, 900)]),
            key);
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        await AssertProblem(mismatch, "idempotency.key.reused");

        var second = await PostMutationAsync(owner, PurchaseOrdersUrl(ctx), body, "po-idem-000002");
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var other = await second.Content.ReadFromJsonAsync<PurchaseOrderDto>(AuthHelper.Json);
        Assert.NotEqual(original.Id, other!.Id);
        Assert.NotEqual(original.Number, other.Number);

        Assert.Equal(2, await CountAuditsAsync(AuditActions.PurchaseOrderCreated, ctx.TenantId));
        Assert.Equal(2, await CountPurchaseOrdersAsync(ctx.StoreId));
    }

    [Fact]
    public async Task Receiving_70_then_30_percent_moves_stock_and_completes_the_order()
    {
        var ctx = await SeedAsync("PURC1");
        using var owner = await OwnerClientAsync(ctx.Email);
        var order = await ApprovedOrderAsync(owner, ctx, "PURC1", (ctx.ProductId, 10, 1200));
        var lineId = order.Items[0].Id;

        var partial = await PostMutationAsync(
            owner,
            ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest("Primera estiba", [new ReceiveGoodsLineRequest(lineId, 7)]),
            "rcv1-key-000001");
        partial.EnsureSuccessStatusCode();
        var firstReceipt = await partial.Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json);
        Assert.NotNull(firstReceipt);
        Assert.StartsWith("GR-", firstReceipt.ReceiptNumber, StringComparison.Ordinal);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, firstReceipt.PurchaseOrderStatusAfter);
        Assert.Equal(ctx.OwnerUserId, firstReceipt.ActorUserId);
        var firstLine = Assert.Single(firstReceipt.Items);
        Assert.Equal(7, firstLine.ReceivedQuantity);
        Assert.Equal(0, firstLine.ReceivedBefore);
        Assert.Equal(7, firstLine.ReceivedAfter);
        Assert.Equal(3, firstLine.RemainingAfter);
        Assert.NotNull(firstLine.InventoryMovementId);
        Assert.Equal(7, await OnHandAsync(ctx.StoreId, ctx.ProductId));

        var rest = await PostMutationAsync(
            owner,
            ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest("Segunda estiba", [new ReceiveGoodsLineRequest(lineId, 3)]),
            "rcv1-key-000002");
        rest.EnsureSuccessStatusCode();
        var secondReceipt = await rest.Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json);
        Assert.Equal(PurchaseOrderStatus.Received, secondReceipt!.PurchaseOrderStatusAfter);
        Assert.Equal(7, secondReceipt.Items[0].ReceivedBefore);
        Assert.Equal(10, secondReceipt.Items[0].ReceivedAfter);
        Assert.Equal(0, secondReceipt.Items[0].RemainingAfter);
        Assert.NotEqual(firstReceipt.ReceiptNumber, secondReceipt.ReceiptNumber);

        var finalOrder = await owner.GetFromJsonAsync<PurchaseOrderDto>(
            $"{PurchaseOrdersUrl(ctx)}/{order.Id}", AuthHelper.Json);
        Assert.Equal(PurchaseOrderStatus.Received, finalOrder!.Status);
        Assert.Equal(10, finalOrder.Items[0].ReceivedQuantity);
        Assert.Equal(0, finalOrder.Items[0].RemainingQuantity);

        Assert.Equal(10, await OnHandAsync(ctx.StoreId, ctx.ProductId));
        var movements = await owner.GetFromJsonAsync<PagedResultDto<InventoryMovementDto>>(
            $"/api/v1/business/stores/{ctx.StoreId}/inventory/{ctx.ProductId}/movements", AuthHelper.Json);
        Assert.NotNull(movements);
        Assert.Equal(2, movements.TotalCount);
        Assert.All(movements.Items, m => Assert.Equal(InventoryMovementType.Receipt, m.Type));
        Assert.Equal(new[] { 3L, 7L }, movements.Items.Select(m => m.OnHandDelta).ToArray());
        Assert.Equal(7, movements.Items[0].OnHandBefore);
        Assert.Equal(10, movements.Items[0].OnHandAfter);

        var receipts = await owner.GetFromJsonAsync<PagedResultDto<GoodsReceiptSummaryDto>>(
            ReceiptsUrl(ctx, order.Id), AuthHelper.Json);
        Assert.NotNull(receipts);
        Assert.Equal(2, receipts.TotalCount);
        Assert.Contains(receipts.Items, r => r.Id == firstReceipt.Id && r.ReceivedQuantity == 7);
        Assert.Contains(receipts.Items, r => r.Id == secondReceipt.Id && r.ReceivedQuantity == 3);

        var detail = await owner.GetFromJsonAsync<GoodsReceiptDto>(
            $"{ReceiptsUrl(ctx, order.Id)}/{firstReceipt.Id}", AuthHelper.Json);
        Assert.Equal(firstReceipt.ReceiptNumber, detail!.ReceiptNumber);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, detail.PurchaseOrderStatusAfter);

        Assert.Equal(2, await CountAuditsAsync(AuditActions.GoodsReceiptRecorded, ctx.TenantId));
        Assert.Equal(1, await CountReceiptMovementsAsync(firstLine.Id));
    }

    [Fact]
    public async Task Receiving_everything_at_once_completes_a_multi_line_order()
    {
        var ctx = await SeedAsync("PURC2");
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var owner = await OwnerClientAsync(ctx.Email);
        var second = await CatalogAdminTests.CreateProductAsync(
            admin, ctx.CategoryId, "SKU-RCV2-B", "Segundo RCV2", null);
        (await owner.PostAsync($"/api/v1/business/stores/{ctx.StoreId}/products/{second.Id}/enable", null))
            .EnsureSuccessStatusCode();

        var order = await ApprovedOrderAsync(
            owner, ctx, "PURC2", (ctx.ProductId, 10, 1200), (second.Id, 5, 800));

        var response = await PostMutationAsync(
            owner,
            ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest(
                null,
                [.. order.Items.Select(i => new ReceiveGoodsLineRequest(i.Id, i.OrderedQuantity))]),
            "rcv2-key-000001");
        response.EnsureSuccessStatusCode();
        var receipt = await response.Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json);
        Assert.NotNull(receipt);
        Assert.Equal(PurchaseOrderStatus.Received, receipt.PurchaseOrderStatusAfter);
        Assert.Equal(2, receipt.Items.Count);
        Assert.All(receipt.Items, i => Assert.Equal(0, i.RemainingAfter));
        Assert.All(receipt.Items, i => Assert.NotNull(i.InventoryMovementId));
        Assert.Equal(
            receipt.Items.Select(i => i.InventoryMovementId).Distinct().Count(),
            receipt.Items.Count);

        Assert.Equal(10, await OnHandAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(5, await OnHandAsync(ctx.StoreId, second.Id));
    }

    [Fact]
    public async Task Over_receipt_is_rejected_and_nothing_is_written()
    {
        var ctx = await SeedAsync("PURC3");
        using var owner = await OwnerClientAsync(ctx.Email);
        var order = await ApprovedOrderAsync(owner, ctx, "PURC3", (ctx.ProductId, 10, 1200));
        var lineId = order.Items[0].Id;

        var tooMuch = await PostMutationAsync(
            owner,
            ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(lineId, 11)]),
            "rcv3-key-000001");
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        await AssertProblem(tooMuch, "goods_receipt.quantity.exceeds_remaining");
        Assert.Equal(0, await CountReceiptsAsync(order.Id));
        Assert.Equal(0, await CountMovementsAsync(ctx.StoreId, ctx.ProductId));
        Assert.Null(await FindIdempotencyAsync(
            ctx.TenantId, IdempotencyOperations.ProcurementReceive, "rcv3-key-000001"));

        (await PostMutationAsync(
            owner,
            ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(lineId, 6)]),
            "rcv3-key-000002")).EnsureSuccessStatusCode();

        var overRemaining = await PostMutationAsync(
            owner,
            ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(lineId, 5)]),
            "rcv3-key-000003");
        Assert.Equal(HttpStatusCode.Conflict, overRemaining.StatusCode);
        await AssertProblem(overRemaining, "goods_receipt.quantity.exceeds_remaining");
        Assert.Equal(6, await OnHandAsync(ctx.StoreId, ctx.ProductId));

        var unknownLine = await PostMutationAsync(
            owner,
            ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(Guid.CreateVersion7(), 1)]),
            "rcv3-key-000004");
        Assert.Equal(HttpStatusCode.Conflict, unknownLine.StatusCode);
        await AssertProblem(unknownLine, "goods_receipt.line.not_found");

        var duplicateLine = await PostMutationAsync(
            owner,
            ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest(
                null,
                [new ReceiveGoodsLineRequest(lineId, 1), new ReceiveGoodsLineRequest(lineId, 2)]),
            "rcv3-key-000005");
        Assert.Equal(HttpStatusCode.Conflict, duplicateLine.StatusCode);
        await AssertProblem(duplicateLine, "goods_receipt.line.duplicate");

        var noLines = await PostMutationAsync(
            owner,
            ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest(null, []),
            "rcv3-key-000006");
        Assert.Equal(HttpStatusCode.BadRequest, noLines.StatusCode);

        var missingKey = await owner.PostAsJsonAsync(
            ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(lineId, 1)]));
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        Assert.Equal(1, await CountReceiptsAsync(order.Id));
    }

    [Fact]
    public async Task Receiving_a_draft_order_is_rejected()
    {
        var ctx = await SeedAsync("PURC4");
        using var owner = await OwnerClientAsync(ctx.Email);
        var supplier = await CreateSupplierAsync(owner, "PURC4");
        var created = await PostMutationAsync(
            owner,
            PurchaseOrdersUrl(ctx),
            new CreatePurchaseOrderRequest(supplier.Id, null, [Line(ctx.ProductId, 4, 1000)]),
            "rcv4-po-000001");
        var order = await created.Content.ReadFromJsonAsync<PurchaseOrderDto>(AuthHelper.Json);

        var response = await PostMutationAsync(
            owner,
            ReceiptsUrl(ctx, order!.Id),
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(order.Items[0].Id, 1)]),
            "rcv4-key-000001");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertProblem(response, "purchase_order.not_receivable");
        Assert.Equal(0, await CountMovementsAsync(ctx.StoreId, ctx.ProductId));
    }

    [Fact]
    public async Task Receiving_initializes_inventory_and_adds_to_existing_stock()
    {
        var ctx = await SeedAsync("PURC5");
        using var owner = await OwnerClientAsync(ctx.Email);
        Assert.Equal(0, await CountInventoryItemsAsync(ctx.StoreId, ctx.ProductId));

        var order = await ApprovedOrderAsync(owner, ctx, "PURC5", (ctx.ProductId, 10, 1000));
        (await PostMutationAsync(
            owner,
            ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(order.Items[0].Id, 4)]),
            "rcv5-key-000001")).EnsureSuccessStatusCode();

        Assert.Equal(1, await CountInventoryItemsAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(4, await OnHandAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(0, await CountAuditsAsync(AuditActions.InventoryInitialized, ctx.TenantId));

        var detail = await owner.GetFromJsonAsync<InventoryItemDto>(
            $"/api/v1/business/stores/{ctx.StoreId}/inventory/{ctx.ProductId}", AuthHelper.Json);
        Assert.NotNull(detail);
        Assert.True(detail.InventoryInitialized);
        Assert.Equal(4, detail.OnHand);
        Assert.Equal(4, detail.Available);

        var adjusted = await PostMutationAsync(
            owner,
            $"/api/v1/business/stores/{ctx.StoreId}/inventory/{ctx.ProductId}/adjustments",
            new AdjustInventoryRequest(InventoryAdjustmentType.Increase, 6, "Conteo físico"),
            "rcv5-adj-000001");
        adjusted.EnsureSuccessStatusCode();
        Assert.Equal(10, await OnHandAsync(ctx.StoreId, ctx.ProductId));

        (await PostMutationAsync(
            owner,
            ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(order.Items[0].Id, 6)]),
            "rcv5-key-000002")).EnsureSuccessStatusCode();

        Assert.Equal(16, await OnHandAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await CountInventoryItemsAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(3, await CountMovementsAsync(ctx.StoreId, ctx.ProductId));
    }

    [Fact]
    public async Task Receiving_the_same_key_twice_replays_the_original_receipt()
    {
        var ctx = await SeedAsync("PURC6");
        using var owner = await OwnerClientAsync(ctx.Email);
        var order = await ApprovedOrderAsync(owner, ctx, "PURC6", (ctx.ProductId, 10, 1000));
        var body = new ReceiveGoodsRequest("Estiba única", [new ReceiveGoodsLineRequest(order.Items[0].Id, 4)]);
        const string key = "rcv6-key-000001";

        var first = await PostMutationAsync(owner, ReceiptsUrl(ctx, order.Id), body, key);
        first.EnsureSuccessStatusCode();
        var original = await first.Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json);

        var replay = await PostMutationAsync(owner, ReceiptsUrl(ctx, order.Id), body, key);
        replay.EnsureSuccessStatusCode();
        var replayed = await replay.Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json);
        Assert.Equal(original!.Id, replayed!.Id);
        Assert.Equal(original.ReceiptNumber, replayed.ReceiptNumber);
        Assert.Equal(original.Items[0].InventoryMovementId, replayed.Items[0].InventoryMovementId);

        var mismatch = await PostMutationAsync(
            owner,
            ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest("Estiba única", [new ReceiveGoodsLineRequest(order.Items[0].Id, 5)]),
            key);
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        await AssertProblem(mismatch, "idempotency.key.reused");

        Assert.Equal(4, await OnHandAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await CountReceiptsAsync(order.Id));
        Assert.Equal(1, await CountMovementsAsync(ctx.StoreId, ctx.ProductId));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.GoodsReceiptRecorded, ctx.TenantId));
    }

    [Fact]
    public async Task Another_tenant_sees_neither_suppliers_nor_orders_nor_receipts()
    {
        var a = await SeedAsync("PUXT1");
        var b = await SeedAsync("PUXT2");
        using var ownerA = await OwnerClientAsync(a.Email);
        using var ownerB = await OwnerClientAsync(b.Email);

        var supplier = await CreateSupplierAsync(ownerA, "PUXT1");
        var order = await ApprovedOrderAsync(ownerA, a, "PUXT1B", (a.ProductId, 5, 1000));
        var receipt = await PostMutationAsync(
            ownerA,
            ReceiptsUrl(a, order.Id),
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(order.Items[0].Id, 2)]),
            "xt1-key-0000001");
        receipt.EnsureSuccessStatusCode();
        var receiptId = (await receipt.Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json))!.Id;

        Assert.Equal(HttpStatusCode.NotFound,
            (await ownerB.GetAsync($"/api/v1/business/suppliers/{supplier.Id}")).StatusCode);
        Assert.Empty((await ownerB.GetFromJsonAsync<PagedResultDto<SupplierDto>>(
            "/api/v1/business/suppliers", AuthHelper.Json))!.Items);

        Assert.Equal(HttpStatusCode.NotFound,
            (await ownerB.GetAsync($"{PurchaseOrdersUrl(a)}/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.GetAsync(PurchaseOrdersUrl(a))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await ownerB.GetAsync($"{ReceiptsUrl(a, order.Id)}/{receiptId}")).StatusCode);

        var foreignStoreOwnOrder = await ownerB.GetAsync(
            $"/api/v1/business/stores/{b.StoreId}/purchase-orders/{order.Id}");
        Assert.Equal(HttpStatusCode.NotFound, foreignStoreOwnOrder.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await PostMutationAsync(
            ownerB,
            ReceiptsUrl(a, order.Id),
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(order.Items[0].Id, 1)]),
            "xt2-key-0000001")).StatusCode);
        Assert.Equal(2, await OnHandAsync(a.StoreId, a.ProductId));

        var ownSupplierCode = await ownerB.PostAsJsonAsync(
            "/api/v1/business/suppliers",
            new CreateSupplierRequest("PROV-XT1", "Mismo código otro tenant", null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Created, ownSupplierCode.StatusCode);
    }

    [Fact]
    public async Task Platform_staff_can_read_purchase_orders_and_receipts_of_any_store()
    {
        var ctx = await SeedAsync("PUADM");
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        using var owner = await OwnerClientAsync(ctx.Email);
        var order = await ApprovedOrderAsync(owner, ctx, "PUADM", (ctx.ProductId, 6, 1100));
        var receipt = await PostMutationAsync(
            owner,
            ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(order.Items[0].Id, 6)]),
            "adm1-key-000001");
        receipt.EnsureSuccessStatusCode();
        var receiptId = (await receipt.Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json))!.Id;

        var list = await admin.GetFromJsonAsync<PagedResultDto<PurchaseOrderSummaryDto>>(
            $"/api/v1/admin/stores/{ctx.StoreId}/purchase-orders", AuthHelper.Json);
        Assert.NotNull(list);
        Assert.Contains(list.Items, o => o.Id == order.Id && o.ReceivedQuantity == 6);

        var detail = await admin.GetFromJsonAsync<PurchaseOrderDto>(
            $"/api/v1/admin/stores/{ctx.StoreId}/purchase-orders/{order.Id}", AuthHelper.Json);
        Assert.Equal(PurchaseOrderStatus.Received, detail!.Status);

        var receipts = await admin.GetFromJsonAsync<PagedResultDto<GoodsReceiptSummaryDto>>(
            $"/api/v1/admin/stores/{ctx.StoreId}/purchase-orders/{order.Id}/receipts", AuthHelper.Json);
        Assert.NotNull(receipts);
        Assert.Single(receipts.Items, r => r.Id == receiptId);

        var receiptDetail = await admin.GetFromJsonAsync<GoodsReceiptDto>(
            $"/api/v1/admin/stores/{ctx.StoreId}/goods-receipts/{receiptId}", AuthHelper.Json);
        Assert.Equal(receiptId, receiptDetail!.Id);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync($"{PurchaseOrdersUrl(ctx)}/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/v1/admin/stores/{Guid.CreateVersion7()}/purchase-orders")).StatusCode);
    }

    [Fact]
    public async Task Database_enforces_receipt_invariants_and_cross_tenant_references()
    {
        var ctx = await SeedAsync("PUDB1");
        var other = await SeedAsync("PUDB2");
        using var owner = await OwnerClientAsync(ctx.Email);
        using var foreignOwner = await OwnerClientAsync(other.Email);
        await CreateSupplierAsync(foreignOwner, "PUDB2");
        var order = await ApprovedOrderAsync(owner, ctx, "PUDB1", (ctx.ProductId, 10, 1000));
        var receiptResponse = await PostMutationAsync(
            owner,
            ReceiptsUrl(ctx, order.Id),
            new ReceiveGoodsRequest(null, [new ReceiveGoodsLineRequest(order.Items[0].Id, 4)]),
            "pdb1-key-000001");
        receiptResponse.EnsureSuccessStatusCode();
        var receipt = (await receiptResponse.Content.ReadFromJsonAsync<GoodsReceiptDto>(AuthHelper.Json))!;

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();

        var item = await db.PurchaseOrderItems.SingleAsync(i => i.Id == order.Items[0].Id);
        Set(item, nameof(PurchaseOrderItem.ReceivedQuantity), 11L);
        var overReceipt = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(
            "ck_purchase_order_items_received_lte_ordered",
            overReceipt.InnerException!.Message,
            StringComparison.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();

        item = await db.PurchaseOrderItems.SingleAsync(i => i.Id == order.Items[0].Id);
        Set(item, nameof(PurchaseOrderItem.UnitCostAmount), 0m);
        var cost = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(
            "ck_purchase_order_items_unit_cost_positive",
            cost.InnerException!.Message,
            StringComparison.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();

        var line = await db.GoodsReceiptItems.SingleAsync(i => i.Id == receipt.Items[0].Id);
        Set(line, nameof(GoodsReceiptItem.ReceivedAfter), 99L);
        var chain = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(
            "ck_goods_receipt_items_received_chain",
            chain.InnerException!.Message,
            StringComparison.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();

        var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == ctx.TenantId);
        var duplicateCode = Supplier.Create(
            ctx.TenantId, supplier.Code, "Duplicado", new SupplierContactDetails(), DateTimeOffset.UtcNow);
        db.Suppliers.Add(duplicateCode);
        var uniqueCode = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(
            "ix_suppliers_tenant_code", uniqueCode.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();

        var foreignSupplier = await db.Suppliers.FirstAsync(s => s.TenantId == other.TenantId);
        var crossTenant = PurchaseOrder.CreateDraft(
            ctx.TenantId,
            ctx.StoreId,
            foreignSupplier.Id,
            "PO-CROSS-TENANT",
            null,
            [new PurchaseOrderLine(ctx.ProductId, 1, Money.Create(1000, CurrencyCodes.Cop))],
            DateTimeOffset.UtcNow);
        db.PurchaseOrders.Add(crossTenant);
        var fk = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("foreign key", fk.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();

        var movement = await db.InventoryMovements.AsNoTracking()
            .SingleAsync(m => m.ReferenceId == receipt.Items[0].Id);
        var inventoryItem = await db.InventoryItems.AsNoTracking()
            .SingleAsync(i => i.StoreId == ctx.StoreId && i.GlobalProductId == ctx.ProductId);
        db.InventoryMovements.Add(DuplicateReceiptMovement(movement, inventoryItem.Id));
        var duplicateReference = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(
            "ix_inventory_movements_reference_unique",
            duplicateReference.InnerException!.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task<SupplierDto> CreateSupplierAsync(HttpClient owner, string suffix)
    {
        var response = await owner.PostAsJsonAsync(
            "/api/v1/business/suppliers",
            new CreateSupplierRequest(
                $"PROV-{suffix}", $"Proveedor {suffix}", null, null, null, null, null, null));
        response.EnsureSuccessStatusCode();
        var supplier = await response.Content.ReadFromJsonAsync<SupplierDto>(AuthHelper.Json);
        Assert.NotNull(supplier);
        return supplier;
    }

    internal static PurchaseOrderItemRequest Line(Guid productId, long quantity, decimal unitCost) =>
        new(productId, quantity, unitCost, "COP");

    internal static string PurchaseOrdersUrl(ProcurementContext ctx) =>
        $"/api/v1/business/stores/{ctx.StoreId}/purchase-orders";

    internal static string ReceiptsUrl(ProcurementContext ctx, Guid purchaseOrderId) =>
        $"{PurchaseOrdersUrl(ctx)}/{purchaseOrderId}/receipts";

    internal static async Task<PurchaseOrderDto> ApprovedOrderAsync(
        HttpClient owner,
        ProcurementContext ctx,
        string suffix,
        params (Guid ProductId, long Quantity, decimal UnitCost)[] lines)
    {
        var supplier = await CreateSupplierAsync(owner, suffix);
        var created = await PostMutationAsync(
            owner,
            PurchaseOrdersUrl(ctx),
            new CreatePurchaseOrderRequest(
                supplier.Id,
                null,
                [.. lines.Select(l => Line(l.ProductId, l.Quantity, l.UnitCost))]),
            $"po-{suffix.ToLowerInvariant()}-000001");
        created.EnsureSuccessStatusCode();
        var order = await created.Content.ReadFromJsonAsync<PurchaseOrderDto>(AuthHelper.Json);
        Assert.NotNull(order);

        var approved = await owner.PostAsync($"{PurchaseOrdersUrl(ctx)}/{order.Id}/approve", null);
        approved.EnsureSuccessStatusCode();
        return (await approved.Content.ReadFromJsonAsync<PurchaseOrderDto>(AuthHelper.Json))!;
    }

    internal static async Task<HttpResponseMessage> PostMutationAsync<T>(
        HttpClient client, string url, T body, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, AuthHelper.Json), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    internal static async Task AssertProblem(HttpResponseMessage response, string code)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(AuthHelper.Json);
        Assert.NotNull(problem);
        Assert.Equal(code, problem.Title);
    }

    internal async Task<ProcurementContext> SeedAsync(string suffix)
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var category = await CatalogAdminTests.CreateCategoryAsync(admin, $"CAT-{suffix}", $"Categoría {suffix}");
        var product = await CatalogAdminTests.CreateProductAsync(
            admin, category.Id, $"SKU-{suffix}", $"Producto {suffix}", null);
        var email = $"owner-{suffix.ToLowerInvariant()}@example.com";
        var franchisee = await CreateFranchiseeAsync(admin, suffix, email, NextIdentification());

        using var owner = await OwnerClientAsync(email);
        (await owner.PostAsync(
            $"/api/v1/business/stores/{franchisee.StoreId}/products/{product.Id}/enable", null))
            .EnsureSuccessStatusCode();

        return new ProcurementContext(
            franchisee.TenantId,
            franchisee.StoreId,
            category.Id,
            product.Id,
            franchisee.OwnerUserId,
            email);
    }

    internal async Task<HttpClient> OwnerClientAsync(string email)
    {
        var client = factory.CreateClientUnredirected();
        var token = await AuthHelper.LoginAsync(client, email, "OwnerTest!23456");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    internal static async Task<CreateFranchiseeResponse> CreateFranchiseeAsync(
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

    /// <summary>
    /// Tax identifications are unique platform-wide, so every seeded franchisee draws a fresh one.
    /// </summary>
    internal static string NextIdentification() =>
        "923" + Interlocked.Increment(ref _identifications).ToString("D6");

    internal async Task<long> OnHandAsync(Guid storeId, Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.InventoryItems
            .Where(i => i.StoreId == storeId && i.GlobalProductId == productId)
            .Select(i => i.OnHand)
            .SingleAsync();
    }

    internal async Task<int> CountInventoryItemsAsync(Guid storeId, Guid productId) =>
        await CountAsync(db => db.InventoryItems.CountAsync(
            i => i.StoreId == storeId && i.GlobalProductId == productId));

    internal async Task<int> CountMovementsAsync(Guid storeId, Guid productId) =>
        await CountAsync(db => db.InventoryMovements.CountAsync(
            m => m.StoreId == storeId && m.GlobalProductId == productId));

    internal async Task<int> CountReceiptMovementsAsync(Guid goodsReceiptItemId) =>
        await CountAsync(db => db.InventoryMovements.CountAsync(
            m => m.Type == InventoryMovementType.Receipt
                 && m.ReferenceType == InventoryReferenceTypes.GoodsReceiptItem
                 && m.ReferenceId == goodsReceiptItemId));

    internal async Task<int> CountPurchaseOrdersAsync(Guid storeId) =>
        await CountAsync(db => db.PurchaseOrders.CountAsync(o => o.StoreId == storeId));

    internal async Task<int> CountReceiptsAsync(Guid purchaseOrderId) =>
        await CountAsync(db => db.GoodsReceipts.CountAsync(r => r.PurchaseOrderId == purchaseOrderId));

    internal async Task<int> CountAuditsAsync(string action, Guid tenantId) =>
        await CountAsync(db => db.Set<AuditEvent>().CountAsync(
            e => e.Action == action && e.TenantId == tenantId));

    internal async Task<IdempotentOperation?> FindIdempotencyAsync(Guid tenantId, string operation, string key)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<ChevrereDbContext>();
        return await db.Set<IdempotentOperation>().AsNoTracking().FirstOrDefaultAsync(
            o => o.TenantId == tenantId && o.Operation == operation && o.IdempotencyKey == key);
    }

    private async Task<int> CountAsync(Func<ChevrereDbContext, Task<int>> count)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        return await count(scope.ServiceProvider.GetRequiredService<ChevrereDbContext>());
    }

    private static InventoryMovement DuplicateReceiptMovement(InventoryMovement source, Guid inventoryItemId)
    {
        var movement = (InventoryMovement)Activator.CreateInstance(typeof(InventoryMovement), nonPublic: true)!;
        Set(movement, nameof(InventoryMovement.Id), Guid.CreateVersion7());
        Set(movement, nameof(InventoryMovement.TenantId), source.TenantId);
        Set(movement, nameof(InventoryMovement.InventoryItemId), inventoryItemId);
        Set(movement, nameof(InventoryMovement.StoreId), source.StoreId);
        Set(movement, nameof(InventoryMovement.GlobalProductId), source.GlobalProductId);
        Set(movement, nameof(InventoryMovement.Type), InventoryMovementType.Receipt);
        Set(movement, nameof(InventoryMovement.OnHandBefore), source.OnHandAfter);
        Set(movement, nameof(InventoryMovement.OnHandAfter), source.OnHandAfter);
        Set(movement, nameof(InventoryMovement.ReservedBefore), 0L);
        Set(movement, nameof(InventoryMovement.ReservedAfter), 0L);
        Set(movement, nameof(InventoryMovement.OnHandDelta), 0L);
        Set(movement, nameof(InventoryMovement.ReservedDelta), 0L);
        Set(movement, nameof(InventoryMovement.ReferenceType), source.ReferenceType);
        Set(movement, nameof(InventoryMovement.ReferenceId), source.ReferenceId);
        Set(movement, nameof(InventoryMovement.CorrelationId), source.CorrelationId);
        Set(movement, nameof(InventoryMovement.OccurredAt), DateTimeOffset.UtcNow);
        Set(movement, nameof(InventoryMovement.CreatedAt), DateTimeOffset.UtcNow);
        Set(movement, nameof(InventoryMovement.UpdatedAt), DateTimeOffset.UtcNow);
        return movement;
    }

    private static void Set(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(
            propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(target, value);
    }
}

internal sealed record ProcurementContext(
    Guid TenantId,
    Guid StoreId,
    Guid CategoryId,
    Guid ProductId,
    Guid OwnerUserId,
    string Email);
