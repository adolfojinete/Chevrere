using Chevrere.Modules.Procurement.Application.Abstractions;
using Chevrere.Modules.Procurement.Application.Contracts;
using Chevrere.Modules.Procurement.Application.Idempotency;
using Chevrere.Modules.Procurement.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Idempotency;
using Chevrere.SharedKernel.Inventory;
using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Results;
using Chevrere.SharedKernel.Time;

namespace Chevrere.Modules.Procurement.Application.Commands;

public sealed record ReceiveGoodsCommand(
    Guid StoreId,
    Guid PurchaseOrderId,
    string? Notes,
    IReadOnlyList<ReceiveGoodsLineRequest> Lines,
    string IdempotencyKey);

/// <summary>
/// Records a supplier delivery. Purchase order quantities, the immutable goods receipt, the inventory
/// ledger, the idempotency row and the audit event are committed in a single SaveChanges: stock can
/// never move without the document that justifies it, and vice versa.
/// </summary>
public sealed class ReceiveGoodsHandler(
    IProcurementStore store,
    IProcurementStoreAccess storeAccess,
    IDocumentNumberGenerator numbers,
    IInventoryInboundService inventory,
    IIdempotencyStore idempotency,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<ReceiveGoodsCommand, Result<GoodsReceiptDto>>
{
    public async Task<Result<GoodsReceiptDto>> HandleAsync(
        ReceiveGoodsCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IdempotencyKeyRules.IsValid(request.IdempotencyKey))
        {
            return Result.Failure<GoodsReceiptDto>(Error.Validation(
                "procurement.idempotency_key.required",
                "Idempotency-Key is required (8-128 chars, no whitespace)."));
        }

        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<GoodsReceiptDto>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        if (await ProcurementGuards.StoreErrorAsync(storeAccess, tenantId, request.StoreId, cancellationToken) is { } storeError)
        {
            return Result.Failure<GoodsReceiptDto>(storeError);
        }

        var hash = ProcurementFingerprints.ForReceive(
            request.StoreId, request.PurchaseOrderId, request.Notes, request.Lines);

        var existingKey = await idempotency.FindAsync(
            tenantId, IdempotencyOperations.ProcurementReceive, request.IdempotencyKey, cancellationToken);
        if (existingKey is not null)
        {
            if (!string.Equals(existingKey.RequestHash, hash, StringComparison.Ordinal))
            {
                return Result.Failure<GoodsReceiptDto>(
                    Error.Conflict("idempotency.key.reused", "Idempotency-Key was already used with a different payload."));
            }

            return await ProcurementIdempotencyReplay.ReplayGoodsReceiptAsync(
                store, existingKey.ResourceId, cancellationToken);
        }

        var order = await store.GetPurchaseOrderAsync(request.PurchaseOrderId, cancellationToken);
        if (order is null || !order.BelongsTo(tenantId, request.StoreId))
        {
            return Result.Failure<GoodsReceiptDto>(Error.NotFound(ErrorCodes.NotFound, "Purchase order not found."));
        }

        var products = order.Items
            .Where(i => request.Lines.Any(l => l.PurchaseOrderItemId == i.Id))
            .Select(i => i.GlobalProductId);
        if (await ProcurementGuards.LinesErrorAsync(
                storeAccess, tenantId, request.StoreId, products, cancellationToken) is { } lineError)
        {
            return Result.Failure<GoodsReceiptDto>(lineError);
        }

        try
        {
            var receipt = order.Receive(
                [.. request.Lines.Select(l => new GoodsReceiptLine(l.PurchaseOrderItemId, l.Quantity))],
                clock.UtcNow);

            var receiptNumber = await numbers.NextGoodsReceiptNumberAsync(tenantId, cancellationToken);
            var document = GoodsReceipt.Record(
                order,
                receiptNumber,
                receipt,
                request.Notes,
                currentUser.UserId,
                correlation.CorrelationId,
                clock.UtcNow);
            store.AddGoodsReceipt(document);

            var inbound = await inventory.ApplyGoodsReceiptAsync(
                new InventoryInboundRequest(
                    tenantId,
                    request.StoreId,
                    currentUser.UserId,
                    correlation.CorrelationId,
                    [.. document.Items.Select(i => new InventoryInboundLine(i.GlobalProductId, i.Id, i.ReceivedQuantity))]),
                cancellationToken);

            foreach (var line in inbound)
            {
                document.LinkInventoryMovement(line.GoodsReceiptItemId, line.MovementId);
            }

            var operation = new IdempotentOperation
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Operation = IdempotencyOperations.ProcurementReceive,
                IdempotencyKey = request.IdempotencyKey,
                RequestHash = hash,
                ResourceId = document.Id,
                CreatedAt = clock.UtcNow
            };
            idempotency.Add(operation);

            audit.Record(
                AuditActions.GoodsReceiptRecorded,
                nameof(GoodsReceipt),
                document.Id,
                tenantId,
                previousValue: new { PurchaseOrderStatus = receipt.StatusBefore },
                newValue: new
                {
                    document.ReceiptNumber,
                    document.PurchaseOrderId,
                    PurchaseOrderStatus = receipt.StatusAfter,
                    Lines = receipt.Lines
                        .Select(l => new
                        {
                            l.PurchaseOrderItemId,
                            l.GlobalProductId,
                            l.ReceivedQuantity,
                            l.ReceivedBefore,
                            l.ReceivedAfter,
                            l.RemainingAfter
                        })
                        .ToList()
                });

            var attempt = new GoodsReceiptAttempt(order, document, inbound, operation);
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is DuplicateKeyException or ConcurrencyConflictException)
            {
                var replay = await ProcurementIdempotencyReplay.TryReplayGoodsReceiptAsync(
                    store,
                    idempotency,
                    audit,
                    inventory,
                    attempt,
                    tenantId,
                    request.IdempotencyKey,
                    hash,
                    cancellationToken);
                if (replay is not null)
                {
                    return replay;
                }

                return Result.Failure<GoodsReceiptDto>(ConcurrencyConflictException.ToError());
            }

            return Result.Success(ProcurementMapping.ToDto(document));
        }
        catch (DomainException ex)
        {
            return Result.Failure<GoodsReceiptDto>(Error.Conflict(ex.Code, ex.Message));
        }
    }
}
