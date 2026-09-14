using Chevrere.Modules.Procurement.Domain;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Domain.ValueObjects;

namespace Chevrere.UnitTests.Procurement;

public sealed class PurchaseOrderTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid SupplierId = Guid.CreateVersion7();
    private static readonly Guid ProductA = Guid.CreateVersion7();
    private static readonly Guid ProductB = Guid.CreateVersion7();

    [Fact]
    public void CreateDraft_starts_in_draft_with_zero_received()
    {
        var order = Draft((ProductA, 10), (ProductB, 5));

        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        Assert.True(order.IsDraft);
        Assert.Null(order.ApprovedAt);
        Assert.Equal(2, order.Items.Count);
        Assert.All(order.Items, item => Assert.Equal(0, item.ReceivedQuantity));
        Assert.Equal(10, order.Items[0].RemainingQuantity);
        Assert.True(order.BelongsTo(TenantId, StoreId));
        Assert.False(order.BelongsTo(TenantId, Guid.CreateVersion7()));
    }

    [Fact]
    public void CreateDraft_rejects_empty_lines_duplicates_and_bad_quantities()
    {
        var empty = Assert.Throws<DomainException>(() => Draft());
        Assert.Equal("purchase_order.items.required", empty.Code);

        var duplicate = Assert.Throws<DomainException>(() => Draft((ProductA, 1), (ProductA, 2)));
        Assert.Equal("purchase_order.item.duplicate", duplicate.Code);

        var quantity = Assert.Throws<DomainException>(() => Draft((ProductA, 0)));
        Assert.Equal("purchase_order.item.quantity.invalid", quantity.Code);
    }

    [Fact]
    public void CreateDraft_requires_tenant_store_supplier_and_number()
    {
        var line = new[] { new PurchaseOrderLine(ProductA, 1, Money.Create(1000, CurrencyCodes.Cop)) };

        Assert.Throws<DomainException>(() => PurchaseOrder.CreateDraft(
            Guid.Empty, StoreId, SupplierId, "PO-1", null, line, FixedClock.Now));
        Assert.Throws<DomainException>(() => PurchaseOrder.CreateDraft(
            TenantId, Guid.Empty, SupplierId, "PO-1", null, line, FixedClock.Now));
        Assert.Throws<DomainException>(() => PurchaseOrder.CreateDraft(
            TenantId, StoreId, Guid.Empty, "PO-1", null, line, FixedClock.Now));
        Assert.Throws<DomainException>(() => PurchaseOrder.CreateDraft(
            TenantId, StoreId, SupplierId, " ", null, line, FixedClock.Now));
    }

    [Fact]
    public void UpdateDraft_replaces_supplier_notes_and_lines()
    {
        var order = Draft((ProductA, 10));
        var newSupplier = Guid.CreateVersion7();

        order.UpdateDraft(
            newSupplier,
            "Pedido semanal",
            [new PurchaseOrderLine(ProductB, 4, Money.Create(2500, CurrencyCodes.Cop))],
            FixedClock.Now.AddMinutes(5));

        Assert.Equal(newSupplier, order.SupplierId);
        Assert.Equal("Pedido semanal", order.Notes);
        var item = Assert.Single(order.Items);
        Assert.Equal(ProductB, item.GlobalProductId);
        Assert.Equal(4, item.OrderedQuantity);
        Assert.Equal(2500m, item.UnitCost().Amount);
    }

    [Fact]
    public void UpdateDraft_keeps_the_line_of_a_product_that_stays_in_the_order()
    {
        var order = Draft((ProductA, 10), (ProductB, 5));
        var keptLineId = order.Items[0].Id;

        order.UpdateDraft(
            SupplierId,
            null,
            [new PurchaseOrderLine(ProductA, 12, Money.Create(1800, CurrencyCodes.Cop))],
            FixedClock.Now.AddMinutes(5));

        var item = Assert.Single(order.Items);
        Assert.Equal(keptLineId, item.Id);
        Assert.Equal(ProductA, item.GlobalProductId);
        Assert.Equal(12, item.OrderedQuantity);
        Assert.Equal(1800m, item.UnitCost().Amount);
    }

    [Fact]
    public void UpdateDraft_is_rejected_once_approved()
    {
        var order = Draft((ProductA, 10));
        order.Approve(FixedClock.Now);

        var ex = Assert.Throws<DomainException>(() => order.UpdateDraft(
            SupplierId,
            null,
            [new PurchaseOrderLine(ProductA, 1, Money.Create(1000, CurrencyCodes.Cop))],
            FixedClock.Now));

        Assert.Equal("purchase_order.not_draft", ex.Code);
    }

    [Fact]
    public void Lines_must_share_a_currency()
    {
        var ex = Assert.Throws<DomainException>(() => PurchaseOrder.CreateDraft(
            TenantId,
            StoreId,
            SupplierId,
            "PO-1",
            null,
            [
                new PurchaseOrderLine(ProductA, 1, Money.Create(1000, CurrencyCodes.Cop)),
                new PurchaseOrderLine(ProductB, 1, Money.FromPersistence(1000, "USD"))
            ],
            FixedClock.Now));

        Assert.Equal("purchase_order.currency.mixed", ex.Code);
    }

    [Fact]
    public void Approve_is_idempotent_and_reports_whether_state_changed()
    {
        var order = Draft((ProductA, 10));

        Assert.True(order.Approve(FixedClock.Now));
        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);
        Assert.Equal(FixedClock.Now, order.ApprovedAt);

        Assert.False(order.Approve(FixedClock.Now.AddMinutes(1)));
        Assert.Equal(FixedClock.Now, order.ApprovedAt);
    }

    [Fact]
    public void Cancel_is_idempotent_and_blocked_after_any_receipt()
    {
        var cancellable = Draft((ProductA, 10));
        Assert.True(cancellable.Cancel("  Proveedor sin stock  ", FixedClock.Now));
        Assert.Equal(PurchaseOrderStatus.Cancelled, cancellable.Status);
        Assert.Equal("Proveedor sin stock", cancellable.CancelReason);
        Assert.Equal(FixedClock.Now, cancellable.CancelledAt);
        Assert.False(cancellable.Cancel("otra razón", FixedClock.Now.AddMinutes(1)));

        var received = Draft((ProductA, 10));
        received.Approve(FixedClock.Now);
        received.Receive([new GoodsReceiptLine(received.Items[0].Id, 3)], FixedClock.Now);

        var ex = Assert.Throws<DomainException>(() => received.Cancel(null, FixedClock.Now));
        Assert.Equal("purchase_order.not_cancellable", ex.Code);
    }

    [Fact]
    public void Cancelled_order_cannot_be_approved_or_receive_goods()
    {
        var order = Draft((ProductA, 10));
        var itemId = order.Items[0].Id;
        order.Cancel(null, FixedClock.Now);

        Assert.Equal("purchase_order.not_approvable", Assert.Throws<DomainException>(
            () => order.Approve(FixedClock.Now)).Code);
        Assert.Equal("purchase_order.not_receivable", Assert.Throws<DomainException>(
            () => order.Receive([new GoodsReceiptLine(itemId, 1)], FixedClock.Now)).Code);
    }

    [Fact]
    public void Draft_cannot_receive_goods()
    {
        var order = Draft((ProductA, 10));

        var ex = Assert.Throws<DomainException>(() => order.Receive(
            [new GoodsReceiptLine(order.Items[0].Id, 1)], FixedClock.Now));

        Assert.Equal("purchase_order.not_receivable", ex.Code);
    }

    [Fact]
    public void Receiving_everything_moves_the_order_to_received()
    {
        var order = Approved((ProductA, 10), (ProductB, 5));

        var receipt = order.Receive(
            [
                new GoodsReceiptLine(order.Items[0].Id, 10),
                new GoodsReceiptLine(order.Items[1].Id, 5)
            ],
            FixedClock.Now);

        Assert.Equal(PurchaseOrderStatus.Approved, receipt.StatusBefore);
        Assert.Equal(PurchaseOrderStatus.Received, receipt.StatusAfter);
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.All(order.Items, item => Assert.True(item.IsFullyReceived));
        Assert.All(receipt.Lines, line => Assert.Equal(0, line.RemainingAfter));
    }

    [Fact]
    public void Partial_receipts_accumulate_and_snapshot_before_after()
    {
        var order = Approved((ProductA, 10));
        var itemId = order.Items[0].Id;

        var first = order.Receive([new GoodsReceiptLine(itemId, 7)], FixedClock.Now);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, first.StatusAfter);
        Assert.Equal(0, first.Lines[0].ReceivedBefore);
        Assert.Equal(7, first.Lines[0].ReceivedAfter);
        Assert.Equal(3, first.Lines[0].RemainingAfter);
        Assert.Equal(ProductA, first.Lines[0].GlobalProductId);

        var second = order.Receive([new GoodsReceiptLine(itemId, 3)], FixedClock.Now.AddHours(1));
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, second.StatusBefore);
        Assert.Equal(PurchaseOrderStatus.Received, second.StatusAfter);
        Assert.Equal(7, second.Lines[0].ReceivedBefore);
        Assert.Equal(10, second.Lines[0].ReceivedAfter);
        Assert.Equal(0, second.Lines[0].RemainingAfter);
    }

    [Fact]
    public void A_partially_received_order_only_completes_when_every_line_is_full()
    {
        var order = Approved((ProductA, 10), (ProductB, 5));

        order.Receive([new GoodsReceiptLine(order.Items[0].Id, 10)], FixedClock.Now);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);

        order.Receive([new GoodsReceiptLine(order.Items[1].Id, 5)], FixedClock.Now.AddHours(1));
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
    }

    [Fact]
    public void Fully_received_order_rejects_further_receipts()
    {
        var order = Approved((ProductA, 4));
        var itemId = order.Items[0].Id;
        order.Receive([new GoodsReceiptLine(itemId, 4)], FixedClock.Now);

        var ex = Assert.Throws<DomainException>(() => order.Receive(
            [new GoodsReceiptLine(itemId, 1)], FixedClock.Now.AddHours(1)));

        Assert.Equal("purchase_order.not_receivable", ex.Code);
    }

    [Fact]
    public void Over_receipt_is_rejected_and_leaves_the_order_untouched()
    {
        var order = Approved((ProductA, 10), (ProductB, 5));

        var ex = Assert.Throws<DomainException>(() => order.Receive(
            [
                new GoodsReceiptLine(order.Items[0].Id, 10),
                new GoodsReceiptLine(order.Items[1].Id, 6)
            ],
            FixedClock.Now));

        Assert.Equal("goods_receipt.quantity.exceeds_remaining", ex.Code);
        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);
        Assert.All(order.Items, item => Assert.Equal(0, item.ReceivedQuantity));
    }

    [Fact]
    public void Over_receipt_across_two_calls_is_rejected()
    {
        var order = Approved((ProductA, 10));
        var itemId = order.Items[0].Id;
        order.Receive([new GoodsReceiptLine(itemId, 6)], FixedClock.Now);

        var ex = Assert.Throws<DomainException>(() => order.Receive(
            [new GoodsReceiptLine(itemId, 5)], FixedClock.Now.AddHours(1)));

        Assert.Equal("goods_receipt.quantity.exceeds_remaining", ex.Code);
        Assert.Equal(6, order.Items[0].ReceivedQuantity);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);
    }

    [Fact]
    public void Receive_rejects_empty_duplicate_unknown_and_non_positive_lines()
    {
        var order = Approved((ProductA, 10));
        var itemId = order.Items[0].Id;

        Assert.Equal("goods_receipt.lines.required", Assert.Throws<DomainException>(
            () => order.Receive([], FixedClock.Now)).Code);
        Assert.Equal("goods_receipt.line.duplicate", Assert.Throws<DomainException>(
            () => order.Receive(
                [new GoodsReceiptLine(itemId, 1), new GoodsReceiptLine(itemId, 2)],
                FixedClock.Now)).Code);
        Assert.Equal("goods_receipt.line.not_found", Assert.Throws<DomainException>(
            () => order.Receive([new GoodsReceiptLine(Guid.CreateVersion7(), 1)], FixedClock.Now)).Code);
        Assert.Equal("goods_receipt.quantity.invalid", Assert.Throws<DomainException>(
            () => order.Receive([new GoodsReceiptLine(itemId, 0)], FixedClock.Now)).Code);

        Assert.Equal(0, order.Items[0].ReceivedQuantity);
    }

    [Fact]
    public void GoodsReceipt_records_the_purchase_order_snapshot()
    {
        var order = Approved((ProductA, 10));
        var receipt = order.Receive([new GoodsReceiptLine(order.Items[0].Id, 4)], FixedClock.Now);

        var goodsReceipt = GoodsReceipt.Record(
            order, "GR-000001", receipt, "  Llegó en dos estibas  ", null, "corr-1", FixedClock.Now);

        Assert.Equal(order.Id, goodsReceipt.PurchaseOrderId);
        Assert.Equal(order.TenantId, goodsReceipt.TenantId);
        Assert.Equal(order.StoreId, goodsReceipt.StoreId);
        Assert.Equal(order.SupplierId, goodsReceipt.SupplierId);
        Assert.Equal("GR-000001", goodsReceipt.ReceiptNumber);
        Assert.Equal("Llegó en dos estibas", goodsReceipt.Notes);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, goodsReceipt.PurchaseOrderStatusAfter);

        var line = Assert.Single(goodsReceipt.Items);
        Assert.Equal(order.Items[0].Id, line.PurchaseOrderItemId);
        Assert.Equal(4, line.ReceivedQuantity);
        Assert.Equal(0, line.ReceivedBefore);
        Assert.Equal(4, line.ReceivedAfter);
        Assert.Equal(6, line.RemainingAfter);
        Assert.Null(line.InventoryMovementId);
    }

    [Fact]
    public void GoodsReceipt_requires_a_number_and_correlation_id()
    {
        var order = Approved((ProductA, 10));
        var receipt = order.Receive([new GoodsReceiptLine(order.Items[0].Id, 4)], FixedClock.Now);

        Assert.Throws<DomainException>(() => GoodsReceipt.Record(
            order, " ", receipt, null, null, "corr-1", FixedClock.Now));
        Assert.Throws<DomainException>(() => GoodsReceipt.Record(
            order, "GR-1", receipt, null, null, " ", FixedClock.Now));
    }

    [Fact]
    public void LinkInventoryMovement_binds_a_line_to_the_inventory_ledger_once()
    {
        var order = Approved((ProductA, 10));
        var receipt = order.Receive([new GoodsReceiptLine(order.Items[0].Id, 4)], FixedClock.Now);
        var goodsReceipt = GoodsReceipt.Record(
            order, "GR-000002", receipt, null, null, "corr-1", FixedClock.Now);
        var movementId = Guid.CreateVersion7();

        goodsReceipt.LinkInventoryMovement(goodsReceipt.Items[0].Id, movementId);
        Assert.Equal(movementId, goodsReceipt.Items[0].InventoryMovementId);

        Assert.Equal("goods_receipt.item.not_found", Assert.Throws<DomainException>(
            () => goodsReceipt.LinkInventoryMovement(Guid.CreateVersion7(), movementId)).Code);
        Assert.Equal("goods_receipt.movement.already_linked", Assert.Throws<DomainException>(
            () => goodsReceipt.LinkInventoryMovement(goodsReceipt.Items[0].Id, Guid.CreateVersion7())).Code);
    }

    private static PurchaseOrder Draft(params (Guid Product, long Quantity)[] lines) =>
        PurchaseOrder.CreateDraft(
            TenantId,
            StoreId,
            SupplierId,
            "PO-000001",
            null,
            [.. lines.Select(l => new PurchaseOrderLine(l.Product, l.Quantity, Money.Create(1500, CurrencyCodes.Cop)))],
            FixedClock.Now);

    private static PurchaseOrder Approved(params (Guid Product, long Quantity)[] lines)
    {
        var order = Draft(lines);
        order.Approve(FixedClock.Now);
        return order;
    }
}
