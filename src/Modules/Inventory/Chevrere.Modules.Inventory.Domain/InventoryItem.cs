using Chevrere.SharedKernel.Domain;

namespace Chevrere.Modules.Inventory.Domain;

/// <summary>
/// Current stock balance for a StoreProduct. Mutations always produce an InventoryMovement (except Initialize(0)).
/// </summary>
public sealed class InventoryItem : AggregateRoot
{
    private InventoryItem()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid GlobalProductId { get; private set; }

    public long OnHand { get; private set; }

    public long Reserved { get; private set; }

    public long Available => OnHand - Reserved;

    public static InventoryItem CreateUninitialized(
        Guid tenantId,
        Guid storeId,
        Guid globalProductId,
        DateTimeOffset utcNow)
    {
        if (tenantId == Guid.Empty || storeId == Guid.Empty || globalProductId == Guid.Empty)
        {
            throw new DomainException("inventory.identity.required", "Tenant, store and product are required.");
        }

        return new InventoryItem
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            StoreId = storeId,
            GlobalProductId = globalProductId,
            OnHand = 0,
            Reserved = 0,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    /// <summary>
    /// Creates the item and optionally an InitialStock movement when quantity &gt; 0.
    /// Initialize(0) creates the item without a zero-delta movement.
    /// </summary>
    public static (InventoryItem Item, InventoryMovement? Movement) Initialize(
        Guid tenantId,
        Guid storeId,
        Guid globalProductId,
        long quantity,
        Guid? actorUserId,
        string correlationId,
        DateTimeOffset utcNow)
    {
        if (quantity < 0)
        {
            throw new DomainException("inventory.quantity.invalid", "Initial quantity cannot be negative.");
        }

        var item = CreateUninitialized(tenantId, storeId, globalProductId, utcNow);
        if (quantity == 0)
        {
            return (item, null);
        }

        var movement = item.Apply(
            InventoryMovementType.InitialStock,
            onHandDelta: quantity,
            reservedDelta: 0,
            reason: null,
            actorUserId,
            correlationId,
            utcNow);
        return (item, movement);
    }

    public InventoryMovement Increase(long quantity, string reason, Guid? actorUserId, string correlationId, DateTimeOffset utcNow)
    {
        EnsurePositiveQuantity(quantity);
        return Apply(
            InventoryMovementType.AdjustmentIncrease,
            onHandDelta: quantity,
            reservedDelta: 0,
            reason: RequireReason(reason),
            actorUserId,
            correlationId,
            utcNow);
    }

    public InventoryMovement Decrease(long quantity, string reason, Guid? actorUserId, string correlationId, DateTimeOffset utcNow)
    {
        EnsurePositiveQuantity(quantity);
        return Apply(
            InventoryMovementType.AdjustmentDecrease,
            onHandDelta: checked(-quantity),
            reservedDelta: 0,
            reason: RequireReason(reason),
            actorUserId,
            correlationId,
            utcNow);
    }

    public InventoryMovement RecordWaste(long quantity, string reason, Guid? actorUserId, string correlationId, DateTimeOffset utcNow)
    {
        EnsurePositiveQuantity(quantity);
        return Apply(
            InventoryMovementType.Waste,
            onHandDelta: checked(-quantity),
            reservedDelta: 0,
            reason: RequireReason(reason),
            actorUserId,
            correlationId,
            utcNow);
    }

    private InventoryMovement Apply(
        InventoryMovementType type,
        long onHandDelta,
        long reservedDelta,
        string? reason,
        Guid? actorUserId,
        string correlationId,
        DateTimeOffset utcNow)
    {
        var movement = InventoryMovement.Create(
            TenantId,
            Id,
            StoreId,
            GlobalProductId,
            type,
            OnHand,
            Reserved,
            onHandDelta,
            reservedDelta,
            reason,
            actorUserId,
            correlationId,
            utcNow);

        OnHand = movement.OnHandAfter;
        Reserved = movement.ReservedAfter;
        UpdatedAt = utcNow;
        return movement;
    }

    private static void EnsurePositiveQuantity(long quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("inventory.quantity.invalid", "Quantity must be greater than zero.");
        }
    }

    private static string RequireReason(string? reason)
    {
        var value = Guard.NotNullOrWhiteSpace(reason, nameof(reason), 500);
        return value;
    }
}
