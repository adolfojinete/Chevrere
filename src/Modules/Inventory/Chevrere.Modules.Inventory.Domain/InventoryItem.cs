using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Inventory;

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
    /// Creates a zero-balance item so an inbound flow (goods receipt) can post its first movement.
    /// Unlike Initialize, it never produces an InitialStock movement.
    /// </summary>
    public static InventoryItem CreateForReceipt(
        Guid tenantId,
        Guid storeId,
        Guid globalProductId,
        DateTimeOffset utcNow) =>
        CreateUninitialized(tenantId, storeId, globalProductId, utcNow);

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
            referenceType: null,
            referenceId: null,
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
            referenceType: null,
            referenceId: null,
            actorUserId,
            correlationId,
            utcNow);
    }

    /// <summary>
    /// Inbound stock from an external document (goods receipt). The reference makes the
    /// ledger entry traceable and lets the database reject a duplicated posting.
    /// </summary>
    public InventoryMovement Receive(
        long quantity,
        string referenceType,
        Guid referenceId,
        Guid? actorUserId,
        string correlationId,
        DateTimeOffset utcNow)
    {
        EnsurePositiveQuantity(quantity);
        if (referenceId == Guid.Empty)
        {
            throw new DomainException("inventory.reference.required", "A receipt movement requires a reference.");
        }

        return Apply(
            InventoryMovementType.Receipt,
            onHandDelta: quantity,
            reservedDelta: 0,
            reason: null,
            referenceType: Guard.NotNullOrWhiteSpace(referenceType, nameof(referenceType), 64),
            referenceId: referenceId,
            actorUserId,
            correlationId,
            utcNow);
    }

    public InventoryMovement Decrease(long quantity, string reason, Guid? actorUserId, string correlationId, DateTimeOffset utcNow)
    {
        EnsurePositiveQuantity(quantity);
        EnsureAvailable(quantity);
        return Apply(
            InventoryMovementType.AdjustmentDecrease,
            onHandDelta: checked(-quantity),
            reservedDelta: 0,
            reason: RequireReason(reason),
            referenceType: null,
            referenceId: null,
            actorUserId,
            correlationId,
            utcNow);
    }

    public InventoryMovement RecordWaste(long quantity, string reason, Guid? actorUserId, string correlationId, DateTimeOffset utcNow)
    {
        EnsurePositiveQuantity(quantity);
        EnsureAvailable(quantity);
        return Apply(
            InventoryMovementType.Waste,
            onHandDelta: checked(-quantity),
            reservedDelta: 0,
            reason: RequireReason(reason),
            referenceType: null,
            referenceId: null,
            actorUserId,
            correlationId,
            utcNow);
    }

    /// <summary>
    /// Holds <paramref name="quantity"/> units so they stop being Available. The ledger reference is
    /// the reservation itself, which lets the unique (type, reference) index reject a double hold.
    /// </summary>
    public InventoryMovement Reserve(
        long quantity,
        Guid reservationId,
        Guid? actorUserId,
        string correlationId,
        DateTimeOffset utcNow)
    {
        EnsurePositiveQuantity(quantity);
        EnsureReservationId(reservationId);
        EnsureAvailable(quantity);
        return Apply(
            InventoryMovementType.Reservation,
            onHandDelta: 0,
            reservedDelta: quantity,
            reason: null,
            referenceType: InventoryReferenceTypes.InventoryReservation,
            referenceId: reservationId,
            actorUserId,
            correlationId,
            utcNow);
    }

    public InventoryMovement ReleaseReservation(
        long quantity,
        Guid reservationId,
        Guid? actorUserId,
        string correlationId,
        DateTimeOffset utcNow)
    {
        EnsurePositiveQuantity(quantity);
        EnsureReservationId(reservationId);
        return Apply(
            InventoryMovementType.ReservationReleased,
            onHandDelta: 0,
            reservedDelta: checked(-quantity),
            reason: null,
            referenceType: InventoryReferenceTypes.InventoryReservation,
            referenceId: reservationId,
            actorUserId,
            correlationId,
            utcNow);
    }

    /// <summary>
    /// Turns a hold into a sale: OnHand and Reserved both drop by <paramref name="quantity"/>.
    /// </summary>
    public InventoryMovement CommitReservation(
        long quantity,
        Guid reservationId,
        Guid? actorUserId,
        string correlationId,
        DateTimeOffset utcNow)
    {
        EnsurePositiveQuantity(quantity);
        EnsureReservationId(reservationId);
        return Apply(
            InventoryMovementType.ReservationCommitted,
            onHandDelta: checked(-quantity),
            reservedDelta: checked(-quantity),
            reason: null,
            referenceType: InventoryReferenceTypes.InventoryReservation,
            referenceId: reservationId,
            actorUserId,
            correlationId,
            utcNow);
    }

    private InventoryMovement Apply(
        InventoryMovementType type,
        long onHandDelta,
        long reservedDelta,
        string? reason,
        string? referenceType,
        Guid? referenceId,
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
            referenceType,
            referenceId,
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

    private void EnsureAvailable(long quantity)
    {
        if (quantity > Available)
        {
            throw new DomainException(
                "inventory.insufficient_available",
                "Available stock is not enough for this quantity.");
        }
    }

    private static void EnsureReservationId(Guid reservationId)
    {
        if (reservationId == Guid.Empty)
        {
            throw new DomainException("inventory.reference.required", "A reservation movement requires a reference.");
        }
    }

    private static string RequireReason(string? reason)
    {
        var value = Guard.NotNullOrWhiteSpace(reason, nameof(reason), 500);
        return value;
    }
}
