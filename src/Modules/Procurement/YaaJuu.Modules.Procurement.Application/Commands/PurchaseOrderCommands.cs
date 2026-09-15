using YaaJuu.Modules.Procurement.Application.Abstractions;
using YaaJuu.Modules.Procurement.Application.Contracts;
using YaaJuu.Modules.Procurement.Application.Idempotency;
using YaaJuu.Modules.Procurement.Domain;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Domain;
using YaaJuu.SharedKernel.Domain.ValueObjects;
using YaaJuu.SharedKernel.Idempotency;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using YaaJuu.SharedKernel.Time;

namespace YaaJuu.Modules.Procurement.Application.Commands;

public sealed record CreatePurchaseOrderCommand(
    Guid StoreId,
    Guid SupplierId,
    string? Notes,
    IReadOnlyList<PurchaseOrderItemRequest> Items,
    string IdempotencyKey);

public sealed class CreatePurchaseOrderHandler(
    IProcurementStore store,
    IProcurementStoreAccess storeAccess,
    IDocumentNumberGenerator numbers,
    IIdempotencyStore idempotency,
    ICurrentUser currentUser,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<CreatePurchaseOrderCommand, Result<PurchaseOrderDto>>
{
    public async Task<Result<PurchaseOrderDto>> HandleAsync(
        CreatePurchaseOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IdempotencyKeyRules.IsValid(request.IdempotencyKey))
        {
            return Result.Failure<PurchaseOrderDto>(Error.Validation(
                "procurement.idempotency_key.required",
                "Idempotency-Key is required (8-128 chars, no whitespace)."));
        }

        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<PurchaseOrderDto>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        if (await ProcurementGuards.StoreErrorAsync(storeAccess, tenantId, request.StoreId, cancellationToken) is { } storeError)
        {
            return Result.Failure<PurchaseOrderDto>(storeError);
        }

        var hash = ProcurementFingerprints.ForPurchaseOrderCreate(
            request.StoreId, request.SupplierId, request.Notes, request.Items);

        var existingKey = await idempotency.FindAsync(
            tenantId, IdempotencyOperations.ProcurementPurchaseOrderCreate, request.IdempotencyKey, cancellationToken);
        if (existingKey is not null)
        {
            if (!string.Equals(existingKey.RequestHash, hash, StringComparison.Ordinal))
            {
                return Result.Failure<PurchaseOrderDto>(
                    Error.Conflict("idempotency.key.reused", "Idempotency-Key was already used with a different payload."));
            }

            return await ProcurementIdempotencyReplay.ReplayPurchaseOrderAsync(
                store, existingKey.ResourceId, cancellationToken);
        }

        var supplier = await store.GetSupplierAsync(request.SupplierId, cancellationToken);
        if (supplier is null || supplier.TenantId != tenantId)
        {
            return Result.Failure<PurchaseOrderDto>(Error.NotFound(ErrorCodes.NotFound, "Supplier not found."));
        }

        var offerings = await ProcurementGuards.LinesErrorAsync(
            storeAccess, tenantId, request.StoreId, request.Items.Select(i => i.GlobalProductId), cancellationToken);
        if (offerings is not null)
        {
            return Result.Failure<PurchaseOrderDto>(offerings);
        }

        try
        {
            supplier.EnsureCanReceiveOrders();
            var lines = ProcurementGuards.ToLines(request.Items);
            var number = await numbers.NextPurchaseOrderNumberAsync(tenantId, cancellationToken);
            var order = PurchaseOrder.CreateDraft(
                tenantId, request.StoreId, request.SupplierId, number, request.Notes, lines, clock.UtcNow);
            store.AddPurchaseOrder(order);

            var operation = new IdempotentOperation
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Operation = IdempotencyOperations.ProcurementPurchaseOrderCreate,
                IdempotencyKey = request.IdempotencyKey,
                RequestHash = hash,
                ResourceId = order.Id,
                CreatedAt = clock.UtcNow
            };
            idempotency.Add(operation);

            audit.Record(
                AuditActions.PurchaseOrderCreated,
                nameof(PurchaseOrder),
                order.Id,
                tenantId,
                previousValue: null,
                newValue: new { order.Number, order.SupplierId, order.Status, LineCount = order.Items.Count });

            var attempt = new PurchaseOrderAttempt(
                order, operation, AuditActions.PurchaseOrderCreated, order.Id);

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is DuplicateKeyException or ConcurrencyConflictException)
            {
                var replay = await ProcurementIdempotencyReplay.TryReplayPurchaseOrderAsync(
                    store,
                    idempotency,
                    audit,
                    attempt,
                    tenantId,
                    request.IdempotencyKey,
                    hash,
                    cancellationToken);
                if (replay is not null)
                {
                    return replay;
                }

                return ex is ConcurrencyConflictException
                    ? Result.Failure<PurchaseOrderDto>(ConcurrencyConflictException.ToError())
                    : Result.Failure<PurchaseOrderDto>(Error.Conflict(
                        "purchase_order.number.duplicate",
                        "A concurrent write conflicted while creating the purchase order."));
            }

            return Result.Success(ProcurementMapping.ToDto(order));
        }
        catch (DomainException ex)
        {
            return Result.Failure<PurchaseOrderDto>(Error.Domain(ex.Code, ex.Message));
        }
    }
}

public sealed record UpdatePurchaseOrderDraftCommand(Guid StoreId, Guid PurchaseOrderId, UpdatePurchaseOrderRequest Request);

public sealed class UpdatePurchaseOrderDraftHandler(
    IProcurementStore store,
    IProcurementStoreAccess storeAccess,
    ICurrentUser currentUser,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<UpdatePurchaseOrderDraftCommand, Result<PurchaseOrderDto>>
{
    public async Task<Result<PurchaseOrderDto>> HandleAsync(
        UpdatePurchaseOrderDraftCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (order, tenantId, error) = await ProcurementGuards.LoadOrderAsync(
            store, storeAccess, currentUser, request.StoreId, request.PurchaseOrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<PurchaseOrderDto>(error!);
        }

        var supplier = await store.GetSupplierAsync(request.Request.SupplierId, cancellationToken);
        if (supplier is null || supplier.TenantId != tenantId)
        {
            return Result.Failure<PurchaseOrderDto>(Error.NotFound(ErrorCodes.NotFound, "Supplier not found."));
        }

        var offerings = await ProcurementGuards.LinesErrorAsync(
            storeAccess,
            tenantId,
            request.StoreId,
            request.Request.Items.Select(i => i.GlobalProductId),
            cancellationToken);
        if (offerings is not null)
        {
            return Result.Failure<PurchaseOrderDto>(offerings);
        }

        var previous = new { order.SupplierId, LineCount = order.Items.Count };
        try
        {
            supplier.EnsureCanReceiveOrders();
            order.UpdateDraft(
                request.Request.SupplierId,
                request.Request.Notes,
                ProcurementGuards.ToLines(request.Request.Items),
                clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure<PurchaseOrderDto>(Error.Domain(ex.Code, ex.Message));
        }

        audit.Record(
            AuditActions.PurchaseOrderUpdated,
            nameof(PurchaseOrder),
            order.Id,
            tenantId,
            previousValue: previous,
            newValue: new { order.SupplierId, LineCount = order.Items.Count });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ProcurementMapping.ToDto(order));
    }
}

public sealed record ApprovePurchaseOrderCommand(Guid StoreId, Guid PurchaseOrderId);

public sealed class ApprovePurchaseOrderHandler(
    IProcurementStore store,
    IProcurementStoreAccess storeAccess,
    ICurrentUser currentUser,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<ApprovePurchaseOrderCommand, Result<PurchaseOrderDto>>
{
    public async Task<Result<PurchaseOrderDto>> HandleAsync(
        ApprovePurchaseOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (order, tenantId, error) = await ProcurementGuards.LoadOrderAsync(
            store, storeAccess, currentUser, request.StoreId, request.PurchaseOrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<PurchaseOrderDto>(error!);
        }

        var previous = order.Status;
        try
        {
            if (!order.Approve(clock.UtcNow))
            {
                return Result.Success(ProcurementMapping.ToDto(order));
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure<PurchaseOrderDto>(Error.Conflict(ex.Code, ex.Message));
        }

        audit.Record(
            AuditActions.PurchaseOrderApproved,
            nameof(PurchaseOrder),
            order.Id,
            tenantId,
            previousValue: new { Status = previous },
            newValue: new { order.Status, order.ApprovedAt });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ProcurementMapping.ToDto(order));
    }
}

public sealed record CancelPurchaseOrderCommand(Guid StoreId, Guid PurchaseOrderId, string? Reason);

public sealed class CancelPurchaseOrderHandler(
    IProcurementStore store,
    IProcurementStoreAccess storeAccess,
    ICurrentUser currentUser,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<CancelPurchaseOrderCommand, Result<PurchaseOrderDto>>
{
    public async Task<Result<PurchaseOrderDto>> HandleAsync(
        CancelPurchaseOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (order, tenantId, error) = await ProcurementGuards.LoadOrderAsync(
            store, storeAccess, currentUser, request.StoreId, request.PurchaseOrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<PurchaseOrderDto>(error!);
        }

        var previous = order.Status;
        try
        {
            if (!order.Cancel(request.Reason, clock.UtcNow))
            {
                return Result.Success(ProcurementMapping.ToDto(order));
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure<PurchaseOrderDto>(Error.Conflict(ex.Code, ex.Message));
        }

        audit.Record(
            AuditActions.PurchaseOrderCancelled,
            nameof(PurchaseOrder),
            order.Id,
            tenantId,
            previousValue: new { Status = previous },
            newValue: new { order.Status, order.CancelReason, order.CancelledAt });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ProcurementMapping.ToDto(order));
    }
}

internal static class ProcurementGuards
{
    public static async Task<Error?> StoreErrorAsync(
        IProcurementStoreAccess storeAccess,
        Guid tenantId,
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var storeTenantId = await storeAccess.GetStoreTenantIdAsync(storeId, cancellationToken);
        return storeTenantId is null || storeTenantId != tenantId
            ? Error.NotFound(ErrorCodes.NotFound, "Store not found.")
            : null;
    }

    /// <summary>
    /// A store can only order what it offers, and only products the platform still keeps active.
    /// </summary>
    public static async Task<Error?> LinesErrorAsync(
        IProcurementStoreAccess storeAccess,
        Guid tenantId,
        Guid storeId,
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken)
    {
        foreach (var productId in productIds.Distinct())
        {
            if (!await storeAccess.StoreProductExistsAsync(tenantId, storeId, productId, cancellationToken))
            {
                return Error.Conflict("procurement.product.not_offered", "The store has not enabled this product.");
            }

            if (!await storeAccess.GlobalProductIsActiveAsync(productId, cancellationToken))
            {
                return Error.Conflict("procurement.product.not_active", "The product is not active.");
            }
        }

        return null;
    }

    public static IReadOnlyList<PurchaseOrderLine> ToLines(IReadOnlyList<PurchaseOrderItemRequest> items) =>
        items
            .Select(i => new PurchaseOrderLine(
                i.GlobalProductId,
                i.OrderedQuantity,
                Money.Create(i.UnitCostAmount, i.UnitCostCurrency)))
            .ToList();

    public static async Task<(PurchaseOrder? Order, Guid TenantId, Error? Error)> LoadOrderAsync(
        IProcurementStore store,
        IProcurementStoreAccess storeAccess,
        ICurrentUser currentUser,
        Guid storeId,
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not Guid tenantId)
        {
            return (null, Guid.Empty, Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        if (await StoreErrorAsync(storeAccess, tenantId, storeId, cancellationToken) is { } storeError)
        {
            return (null, tenantId, storeError);
        }

        var order = await store.GetPurchaseOrderAsync(purchaseOrderId, cancellationToken);
        if (order is null || !order.BelongsTo(tenantId, storeId))
        {
            return (null, tenantId, Error.NotFound(ErrorCodes.NotFound, "Purchase order not found."));
        }

        return (order, tenantId, null);
    }
}
