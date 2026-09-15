using YaaJuu.Modules.Inventory.Application.Abstractions;
using YaaJuu.Modules.Inventory.Application.Contracts;
using YaaJuu.Modules.Inventory.Application.Idempotency;
using YaaJuu.Modules.Inventory.Domain;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Domain;
using YaaJuu.SharedKernel.Idempotency;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using YaaJuu.SharedKernel.Time;

namespace YaaJuu.Modules.Inventory.Application.Commands;

public sealed record InitializeInventoryCommand(
    Guid StoreId,
    Guid GlobalProductId,
    long Quantity,
    string IdempotencyKey);

public sealed class InitializeInventoryHandler(
    IInventoryStore store,
    IInventoryStoreAccess storeAccess,
    IIdempotencyStore idempotency,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<InitializeInventoryCommand, Result<InventoryMutationDto>>
{
    public async Task<Result<InventoryMutationDto>> HandleAsync(
        InitializeInventoryCommand request,
        CancellationToken cancellationToken)
    {
        if (!IdempotencyKeyRules.IsValid(request.IdempotencyKey))
        {
            return Result.Failure<InventoryMutationDto>(
                Error.Validation("inventory.idempotency_key.required", "Idempotency-Key is required (8-128 chars, no whitespace)."));
        }

        if (currentUser.TenantId is not Guid tenantId || currentUser.UserId is null)
        {
            return Result.Failure<InventoryMutationDto>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var access = await EnsureStoreProductAsync(storeAccess, tenantId, request.StoreId, request.GlobalProductId, cancellationToken);
        if (access is not null)
        {
            return Result.Failure<InventoryMutationDto>(access);
        }

        var hash = IdempotencyFingerprint.Sha256(
            IdempotencyOperations.InventoryInitialize,
            request.StoreId.ToString("D"),
            request.GlobalProductId.ToString("D"),
            IdempotencyFingerprint.Format(request.Quantity));

        var existingKey = await idempotency.FindAsync(
            tenantId,
            IdempotencyOperations.InventoryInitialize,
            request.IdempotencyKey,
            cancellationToken);
        if (existingKey is not null)
        {
            if (!string.Equals(existingKey.RequestHash, hash, StringComparison.Ordinal))
            {
                return Result.Failure<InventoryMutationDto>(
                    Error.Conflict("idempotency.key.reused", "Idempotency-Key was already used with a different payload."));
            }

            return await InventoryIdempotencyReplay.ReplayFromWinnerAsync(
                store,
                IdempotencyOperations.InventoryInitialize,
                existingKey.ResourceId,
                request.Quantity,
                cancellationToken);
        }

        if (await store.GetItemAsync(request.StoreId, request.GlobalProductId, cancellationToken) is not null)
        {
            return Result.Failure<InventoryMutationDto>(
                Error.Conflict("inventory.already_initialized", "Inventory has already been initialized for this product."));
        }

        try
        {
            var (item, movement) = InventoryItem.Initialize(
                tenantId,
                request.StoreId,
                request.GlobalProductId,
                request.Quantity,
                currentUser.UserId,
                correlation.CorrelationId,
                clock.UtcNow);

            store.AddItem(item);
            if (movement is not null)
            {
                store.AddMovement(movement);
            }

            var resourceId = movement?.Id ?? item.Id;
            var op = new IdempotentOperation
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Operation = IdempotencyOperations.InventoryInitialize,
                IdempotencyKey = request.IdempotencyKey,
                RequestHash = hash,
                ResourceId = resourceId,
                CreatedAt = clock.UtcNow
            };
            idempotency.Add(op);

            audit.Record(
                AuditActions.InventoryInitialized,
                movement is null ? nameof(InventoryItem) : nameof(InventoryMovement),
                resourceId,
                tenantId,
                previousValue: null,
                newValue: new { item.OnHand, item.Reserved, request.Quantity });

            var attempt = new InventoryMutationAttempt(
                item,
                movement,
                op,
                AuditActions.InventoryInitialized,
                movement is null ? nameof(InventoryItem) : nameof(InventoryMovement),
                resourceId);

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DuplicateKeyException)
            {
                var replay = await InventoryIdempotencyReplay.TryReplayAfterWriteConflictAsync(
                    store,
                    idempotency,
                    audit,
                    attempt,
                    tenantId,
                    IdempotencyOperations.InventoryInitialize,
                    request.IdempotencyKey,
                    hash,
                    request.Quantity,
                    cancellationToken);
                if (replay is not null)
                {
                    return replay;
                }

                return Result.Failure<InventoryMutationDto>(
                    Error.Conflict("inventory.already_initialized", "Inventory has already been initialized for this product."));
            }
            catch (ConcurrencyConflictException)
            {
                var replay = await InventoryIdempotencyReplay.TryReplayAfterWriteConflictAsync(
                    store,
                    idempotency,
                    audit,
                    attempt,
                    tenantId,
                    IdempotencyOperations.InventoryInitialize,
                    request.IdempotencyKey,
                    hash,
                    request.Quantity,
                    cancellationToken);
                if (replay is not null)
                {
                    return replay;
                }

                return Result.Failure<InventoryMutationDto>(ConcurrencyConflictException.ToError());
            }

            return Result.Success(InventoryIdempotencyReplay.FromItem(item, movement?.Id));
        }
        catch (DomainException ex)
        {
            return Result.Failure<InventoryMutationDto>(Error.Conflict(ex.Code, ex.Message));
        }
    }

    internal static async Task<Error?> EnsureStoreProductAsync(
        IInventoryStoreAccess storeAccess,
        Guid tenantId,
        Guid storeId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var storeTenantId = await storeAccess.GetStoreTenantIdAsync(storeId, cancellationToken);
        if (storeTenantId is null || storeTenantId != tenantId)
        {
            return Error.NotFound(ErrorCodes.NotFound, "Store not found.");
        }

        if (!await storeAccess.StoreProductExistsAsync(tenantId, storeId, productId, cancellationToken))
        {
            return Error.Conflict("inventory.product.not_offered", "The store has not enabled this product.");
        }

        return null;
    }
}

public sealed record AdjustInventoryCommand(
    Guid StoreId,
    Guid GlobalProductId,
    InventoryAdjustmentType Type,
    long Quantity,
    string Reason,
    string IdempotencyKey);

public sealed class AdjustInventoryHandler(
    IInventoryStore store,
    IInventoryStoreAccess storeAccess,
    IIdempotencyStore idempotency,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<AdjustInventoryCommand, Result<InventoryMutationDto>>
{
    public async Task<Result<InventoryMutationDto>> HandleAsync(
        AdjustInventoryCommand request,
        CancellationToken cancellationToken)
    {
        if (!IdempotencyKeyRules.IsValid(request.IdempotencyKey))
        {
            return Result.Failure<InventoryMutationDto>(
                Error.Validation("inventory.idempotency_key.required", "Idempotency-Key is required (8-128 chars, no whitespace)."));
        }

        if (currentUser.TenantId is not Guid tenantId || currentUser.UserId is null)
        {
            return Result.Failure<InventoryMutationDto>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var access = await InitializeInventoryHandler.EnsureStoreProductAsync(
            storeAccess, tenantId, request.StoreId, request.GlobalProductId, cancellationToken);
        if (access is not null)
        {
            return Result.Failure<InventoryMutationDto>(access);
        }

        var reason = request.Reason.Trim();
        var hash = IdempotencyFingerprint.Sha256(
            IdempotencyOperations.InventoryAdjust,
            request.StoreId.ToString("D"),
            request.GlobalProductId.ToString("D"),
            request.Type.ToString(),
            IdempotencyFingerprint.Format(request.Quantity),
            reason);

        var existingKey = await idempotency.FindAsync(
            tenantId, IdempotencyOperations.InventoryAdjust, request.IdempotencyKey, cancellationToken);
        if (existingKey is not null)
        {
            if (!string.Equals(existingKey.RequestHash, hash, StringComparison.Ordinal))
            {
                return Result.Failure<InventoryMutationDto>(
                    Error.Conflict("idempotency.key.reused", "Idempotency-Key was already used with a different payload."));
            }

            return await InventoryIdempotencyReplay.ReplayFromWinnerAsync(
                store, IdempotencyOperations.InventoryAdjust, existingKey.ResourceId, null, cancellationToken);
        }

        var item = await store.GetItemAsync(request.StoreId, request.GlobalProductId, cancellationToken);
        if (item is null)
        {
            return Result.Failure<InventoryMutationDto>(
                Error.Conflict("inventory.not_initialized", "Inventory has not been initialized for this product."));
        }

        try
        {
            var before = item.OnHand;
            var movement = request.Type == InventoryAdjustmentType.Increase
                ? item.Increase(request.Quantity, reason, currentUser.UserId, correlation.CorrelationId, clock.UtcNow)
                : item.Decrease(request.Quantity, reason, currentUser.UserId, correlation.CorrelationId, clock.UtcNow);

            store.AddMovement(movement);
            var op = new IdempotentOperation
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Operation = IdempotencyOperations.InventoryAdjust,
                IdempotencyKey = request.IdempotencyKey,
                RequestHash = hash,
                ResourceId = movement.Id,
                CreatedAt = clock.UtcNow
            };
            idempotency.Add(op);

            audit.Record(
                AuditActions.InventoryAdjusted,
                nameof(InventoryMovement),
                movement.Id,
                tenantId,
                previousValue: new { OnHand = before },
                newValue: new { movement.OnHandAfter, Delta = movement.OnHandDelta, Reason = movement.Reason });

            var attempt = new InventoryMutationAttempt(
                item,
                movement,
                op,
                AuditActions.InventoryAdjusted,
                nameof(InventoryMovement),
                movement.Id);

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DuplicateKeyException)
            {
                var replay = await InventoryIdempotencyReplay.TryReplayAfterWriteConflictAsync(
                    store,
                    idempotency,
                    audit,
                    attempt,
                    tenantId,
                    IdempotencyOperations.InventoryAdjust,
                    request.IdempotencyKey,
                    hash,
                    null,
                    cancellationToken);
                if (replay is not null)
                {
                    return replay;
                }

                throw;
            }
            catch (ConcurrencyConflictException)
            {
                var replay = await InventoryIdempotencyReplay.TryReplayAfterWriteConflictAsync(
                    store,
                    idempotency,
                    audit,
                    attempt,
                    tenantId,
                    IdempotencyOperations.InventoryAdjust,
                    request.IdempotencyKey,
                    hash,
                    null,
                    cancellationToken);
                if (replay is not null)
                {
                    return replay;
                }

                return Result.Failure<InventoryMutationDto>(ConcurrencyConflictException.ToError());
            }

            return Result.Success(InventoryIdempotencyReplay.FromMovement(movement));
        }
        catch (DomainException ex)
        {
            return Result.Failure<InventoryMutationDto>(Error.Conflict(ex.Code, ex.Message));
        }
    }
}

public sealed record WasteInventoryCommand(
    Guid StoreId,
    Guid GlobalProductId,
    long Quantity,
    string Reason,
    string IdempotencyKey);

public sealed class WasteInventoryHandler(
    IInventoryStore store,
    IInventoryStoreAccess storeAccess,
    IIdempotencyStore idempotency,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<WasteInventoryCommand, Result<InventoryMutationDto>>
{
    public async Task<Result<InventoryMutationDto>> HandleAsync(
        WasteInventoryCommand request,
        CancellationToken cancellationToken)
    {
        if (!IdempotencyKeyRules.IsValid(request.IdempotencyKey))
        {
            return Result.Failure<InventoryMutationDto>(
                Error.Validation("inventory.idempotency_key.required", "Idempotency-Key is required (8-128 chars, no whitespace)."));
        }

        if (currentUser.TenantId is not Guid tenantId || currentUser.UserId is null)
        {
            return Result.Failure<InventoryMutationDto>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var access = await InitializeInventoryHandler.EnsureStoreProductAsync(
            storeAccess, tenantId, request.StoreId, request.GlobalProductId, cancellationToken);
        if (access is not null)
        {
            return Result.Failure<InventoryMutationDto>(access);
        }

        var reason = request.Reason.Trim();
        var hash = IdempotencyFingerprint.Sha256(
            IdempotencyOperations.InventoryWaste,
            request.StoreId.ToString("D"),
            request.GlobalProductId.ToString("D"),
            IdempotencyFingerprint.Format(request.Quantity),
            reason);

        var existingKey = await idempotency.FindAsync(
            tenantId, IdempotencyOperations.InventoryWaste, request.IdempotencyKey, cancellationToken);
        if (existingKey is not null)
        {
            if (!string.Equals(existingKey.RequestHash, hash, StringComparison.Ordinal))
            {
                return Result.Failure<InventoryMutationDto>(
                    Error.Conflict("idempotency.key.reused", "Idempotency-Key was already used with a different payload."));
            }

            return await InventoryIdempotencyReplay.ReplayFromWinnerAsync(
                store, IdempotencyOperations.InventoryWaste, existingKey.ResourceId, null, cancellationToken);
        }

        var item = await store.GetItemAsync(request.StoreId, request.GlobalProductId, cancellationToken);
        if (item is null)
        {
            return Result.Failure<InventoryMutationDto>(
                Error.Conflict("inventory.not_initialized", "Inventory has not been initialized for this product."));
        }

        try
        {
            var before = item.OnHand;
            var movement = item.RecordWaste(
                request.Quantity, reason, currentUser.UserId, correlation.CorrelationId, clock.UtcNow);
            store.AddMovement(movement);
            var op = new IdempotentOperation
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Operation = IdempotencyOperations.InventoryWaste,
                IdempotencyKey = request.IdempotencyKey,
                RequestHash = hash,
                ResourceId = movement.Id,
                CreatedAt = clock.UtcNow
            };
            idempotency.Add(op);

            audit.Record(
                AuditActions.InventoryWasteRecorded,
                nameof(InventoryMovement),
                movement.Id,
                tenantId,
                previousValue: new { OnHand = before },
                newValue: new { movement.OnHandAfter, Delta = movement.OnHandDelta, Reason = movement.Reason });

            var attempt = new InventoryMutationAttempt(
                item,
                movement,
                op,
                AuditActions.InventoryWasteRecorded,
                nameof(InventoryMovement),
                movement.Id);

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DuplicateKeyException)
            {
                var replay = await InventoryIdempotencyReplay.TryReplayAfterWriteConflictAsync(
                    store,
                    idempotency,
                    audit,
                    attempt,
                    tenantId,
                    IdempotencyOperations.InventoryWaste,
                    request.IdempotencyKey,
                    hash,
                    null,
                    cancellationToken);
                if (replay is not null)
                {
                    return replay;
                }

                throw;
            }
            catch (ConcurrencyConflictException)
            {
                var replay = await InventoryIdempotencyReplay.TryReplayAfterWriteConflictAsync(
                    store,
                    idempotency,
                    audit,
                    attempt,
                    tenantId,
                    IdempotencyOperations.InventoryWaste,
                    request.IdempotencyKey,
                    hash,
                    null,
                    cancellationToken);
                if (replay is not null)
                {
                    return replay;
                }

                return Result.Failure<InventoryMutationDto>(ConcurrencyConflictException.ToError());
            }

            return Result.Success(InventoryIdempotencyReplay.FromMovement(movement));
        }
        catch (DomainException ex)
        {
            return Result.Failure<InventoryMutationDto>(Error.Conflict(ex.Code, ex.Message));
        }
    }
}
