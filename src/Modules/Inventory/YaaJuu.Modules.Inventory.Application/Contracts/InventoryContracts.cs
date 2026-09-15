using YaaJuu.Modules.Inventory.Domain;

namespace YaaJuu.Modules.Inventory.Application.Contracts;

public enum InventoryAdjustmentType
{
    Increase = 1,
    Decrease = 2
}

public sealed record InitializeInventoryRequest(long Quantity);

public sealed record AdjustInventoryRequest(InventoryAdjustmentType Type, long Quantity, string Reason);

public sealed record WasteInventoryRequest(long Quantity, string Reason);

public sealed record InventoryItemDto(
    Guid? InventoryItemId,
    Guid StoreId,
    Guid GlobalProductId,
    string Sku,
    string Name,
    bool IsStoreProductEnabled,
    bool InventoryInitialized,
    long OnHand,
    long Reserved,
    long Available);

public sealed record InventoryMutationDto(
    Guid InventoryItemId,
    Guid? MovementId,
    long OnHand,
    long Reserved,
    long Available);

public sealed record InventoryMovementDto(
    Guid Id,
    InventoryMovementType Type,
    long OnHandDelta,
    long ReservedDelta,
    long OnHandBefore,
    long OnHandAfter,
    long ReservedBefore,
    long ReservedAfter,
    string? Reason,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId);
