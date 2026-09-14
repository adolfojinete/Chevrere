using Chevrere.Modules.Inventory.Application.Abstractions;
using Chevrere.Modules.Inventory.Application.Contracts;
using Chevrere.Modules.Inventory.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Idempotency;
using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Results;
using Chevrere.SharedKernel.Time;

namespace Chevrere.Modules.Inventory.Application.Commands;

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
            request.Quantity.ToString());

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

            return await ReplayInitializeAsync(store, request.StoreId, request.GlobalProductId, existingKey.ResourceId, cancellationToken);
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
            idempotency.Add(new IdempotentOperation
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Operation = IdempotencyOperations.InventoryInitialize,
                IdempotencyKey = request.IdempotencyKey,
                RequestHash = hash,
                ResourceId = resourceId,
                CreatedAt = clock.UtcNow
            });

            audit.Record(
                AuditActions.InventoryInitialized,
                movement is null ? nameof(InventoryItem) : nameof(InventoryMovement),
                resourceId,
                tenantId,
                previousValue: null,
                newValue: new { item.OnHand, item.Reserved, request.Quantity });

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DuplicateKeyException)
            {
                var raced = await idempotency.FindAsync(
                    tenantId,
                    IdempotencyOperations.InventoryInitialize,
                    request.IdempotencyKey,
                    cancellationToken);
                if (raced is not null && string.Equals(raced.RequestHash, hash, StringComparison.Ordinal))
                {
                    return await ReplayInitializeAsync(store, request.StoreId, request.GlobalProductId, raced.ResourceId, cancellationToken);
                }

                return Result.Failure<InventoryMutationDto>(
                    Error.Conflict("inventory.already_initialized", "Inventory has already been initialized for this product."));
            }

            return Result.Success(ToMutation(item, movement?.Id));
        }
        catch (DomainException ex)
        {
            return Result.Failure<InventoryMutationDto>(Error.Conflict(ex.Code, ex.Message));
        }
    }

    private static async Task<Result<InventoryMutationDto>> ReplayInitializeAsync(
        IInventoryStore store,
        Guid storeId,
        Guid productId,
        Guid? resourceId,
        CancellationToken cancellationToken)
    {
        var item = await store.GetItemAsync(storeId, productId, cancellationToken);
        if (item is null)
        {
            return Result.Failure<InventoryMutationDto>(
                Error.Conflict(ErrorCodes.Conflict, "Idempotent initialize could not be replayed."));
        }

        Guid? movementId = null;
        if (resourceId is Guid id && id != item.Id)
        {
            movementId = id;
        }

        return Result.Success(ToMutation(item, movementId));
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

    private static InventoryMutationDto ToMutation(InventoryItem item, Guid? movementId) =>
        new(item.Id, movementId, item.OnHand, item.Reserved, item.Available);
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

        var hash = IdempotencyFingerprint.Sha256(
            IdempotencyOperations.InventoryAdjust,
            request.StoreId.ToString("D"),
            request.GlobalProductId.ToString("D"),
            request.Type.ToString(),
            request.Quantity.ToString(),
            request.Reason.Trim());

        var existingKey = await idempotency.FindAsync(
            tenantId, IdempotencyOperations.InventoryAdjust, request.IdempotencyKey, cancellationToken);
        if (existingKey is not null)
        {
            if (!string.Equals(existingKey.RequestHash, hash, StringComparison.Ordinal))
            {
                return Result.Failure<InventoryMutationDto>(
                    Error.Conflict("idempotency.key.reused", "Idempotency-Key was already used with a different payload."));
            }

            return await ReplayMutationAsync(store, request.StoreId, request.GlobalProductId, existingKey.ResourceId, cancellationToken);
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
                ? item.Increase(request.Quantity, request.Reason, currentUser.UserId, correlation.CorrelationId, clock.UtcNow)
                : item.Decrease(request.Quantity, request.Reason, currentUser.UserId, correlation.CorrelationId, clock.UtcNow);

            store.AddMovement(movement);
            idempotency.Add(new IdempotentOperation
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Operation = IdempotencyOperations.InventoryAdjust,
                IdempotencyKey = request.IdempotencyKey,
                RequestHash = hash,
                ResourceId = movement.Id,
                CreatedAt = clock.UtcNow
            });

            audit.Record(
                AuditActions.InventoryAdjusted,
                nameof(InventoryMovement),
                movement.Id,
                tenantId,
                previousValue: new { OnHand = before },
                newValue: new { movement.OnHandAfter, Delta = movement.OnHandDelta, Reason = movement.Reason });

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DuplicateKeyException)
            {
                var raced = await idempotency.FindAsync(
                    tenantId, IdempotencyOperations.InventoryAdjust, request.IdempotencyKey, cancellationToken);
                if (raced is not null && string.Equals(raced.RequestHash, hash, StringComparison.Ordinal))
                {
                    return await ReplayMutationAsync(store, request.StoreId, request.GlobalProductId, raced.ResourceId, cancellationToken);
                }

                throw;
            }

            return Result.Success(new InventoryMutationDto(item.Id, movement.Id, item.OnHand, item.Reserved, item.Available));
        }
        catch (DomainException ex)
        {
            return Result.Failure<InventoryMutationDto>(Error.Conflict(ex.Code, ex.Message));
        }
    }

    internal static async Task<Result<InventoryMutationDto>> ReplayMutationAsync(
        IInventoryStore store,
        Guid storeId,
        Guid productId,
        Guid? movementId,
        CancellationToken cancellationToken)
    {
        var item = await store.GetItemAsync(storeId, productId, cancellationToken);
        if (item is null)
        {
            return Result.Failure<InventoryMutationDto>(
                Error.Conflict(ErrorCodes.Conflict, "Idempotent mutation could not be replayed."));
        }

        return Result.Success(new InventoryMutationDto(item.Id, movementId, item.OnHand, item.Reserved, item.Available));
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

        var hash = IdempotencyFingerprint.Sha256(
            IdempotencyOperations.InventoryWaste,
            request.StoreId.ToString("D"),
            request.GlobalProductId.ToString("D"),
            request.Quantity.ToString(),
            request.Reason.Trim());

        var existingKey = await idempotency.FindAsync(
            tenantId, IdempotencyOperations.InventoryWaste, request.IdempotencyKey, cancellationToken);
        if (existingKey is not null)
        {
            if (!string.Equals(existingKey.RequestHash, hash, StringComparison.Ordinal))
            {
                return Result.Failure<InventoryMutationDto>(
                    Error.Conflict("idempotency.key.reused", "Idempotency-Key was already used with a different payload."));
            }

            return await AdjustInventoryHandler.ReplayMutationAsync(
                store, request.StoreId, request.GlobalProductId, existingKey.ResourceId, cancellationToken);
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
                request.Quantity, request.Reason, currentUser.UserId, correlation.CorrelationId, clock.UtcNow);
            store.AddMovement(movement);
            idempotency.Add(new IdempotentOperation
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Operation = IdempotencyOperations.InventoryWaste,
                IdempotencyKey = request.IdempotencyKey,
                RequestHash = hash,
                ResourceId = movement.Id,
                CreatedAt = clock.UtcNow
            });

            audit.Record(
                AuditActions.InventoryWasteRecorded,
                nameof(InventoryMovement),
                movement.Id,
                tenantId,
                previousValue: new { OnHand = before },
                newValue: new { movement.OnHandAfter, Delta = movement.OnHandDelta, Reason = movement.Reason });

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DuplicateKeyException)
            {
                var raced = await idempotency.FindAsync(
                    tenantId, IdempotencyOperations.InventoryWaste, request.IdempotencyKey, cancellationToken);
                if (raced is not null && string.Equals(raced.RequestHash, hash, StringComparison.Ordinal))
                {
                    return await AdjustInventoryHandler.ReplayMutationAsync(
                        store, request.StoreId, request.GlobalProductId, raced.ResourceId, cancellationToken);
                }

                throw;
            }

            return Result.Success(new InventoryMutationDto(item.Id, movement.Id, item.OnHand, item.Reserved, item.Available));
        }
        catch (DomainException ex)
        {
            return Result.Failure<InventoryMutationDto>(Error.Conflict(ex.Code, ex.Message));
        }
    }
}
