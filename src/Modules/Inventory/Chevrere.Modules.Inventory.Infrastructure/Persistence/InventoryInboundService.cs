using Chevrere.Modules.Inventory.Application.Abstractions;
using Chevrere.Modules.Inventory.Domain;
using Chevrere.SharedKernel.Inventory;
using Chevrere.SharedKernel.Time;

namespace Chevrere.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// Applies inbound stock for another module's document. Stages balances and ledger entries in the
/// caller's unit of work; the caller commits everything (document, ledger, idempotency, audit) at once.
/// </summary>
public sealed class InventoryInboundService(IInventoryStore store, IClock clock) : IInventoryInboundService
{
    private readonly Dictionary<Guid, StagedWrite> _staged = [];

    public async Task<IReadOnlyList<InventoryInboundLineResult>> ApplyGoodsReceiptAsync(
        InventoryInboundRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var utcNow = clock.UtcNow;
        var touched = new Dictionary<Guid, InventoryItem>();
        var results = new List<InventoryInboundLineResult>(request.Lines.Count);

        foreach (var line in request.Lines)
        {
            var item = await ResolveItemAsync(request, line, touched, utcNow, cancellationToken);
            var movement = item.Receive(
                line.Quantity,
                InventoryReferenceTypes.GoodsReceiptItem,
                line.GoodsReceiptItemId,
                request.ActorUserId,
                request.CorrelationId,
                utcNow);
            store.AddMovement(movement);

            _staged[movement.Id] = new StagedWrite(item, movement);
            results.Add(new InventoryInboundLineResult(
                line.GlobalProductId,
                line.GoodsReceiptItemId,
                item.Id,
                movement.Id,
                movement.OnHandAfter,
                movement.ReservedAfter));
        }

        return results;
    }

    public void DiscardPending(IReadOnlyList<InventoryInboundLineResult> applied)
    {
        ArgumentNullException.ThrowIfNull(applied);

        foreach (var result in applied)
        {
            if (!_staged.Remove(result.MovementId, out var staged))
            {
                continue;
            }

            store.DiscardMovement(staged.Movement);
            store.DiscardItem(staged.Item);
        }
    }

    private async Task<InventoryItem> ResolveItemAsync(
        InventoryInboundRequest request,
        InventoryInboundLine line,
        Dictionary<Guid, InventoryItem> touched,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        if (touched.TryGetValue(line.GlobalProductId, out var known))
        {
            return known;
        }

        var item = await store.GetItemAsync(request.StoreId, line.GlobalProductId, cancellationToken);
        if (item is null)
        {
            item = InventoryItem.CreateForReceipt(
                request.TenantId,
                request.StoreId,
                line.GlobalProductId,
                utcNow);
            store.AddItem(item);
        }

        touched[line.GlobalProductId] = item;
        return item;
    }

    private sealed record StagedWrite(InventoryItem Item, InventoryMovement Movement);
}
