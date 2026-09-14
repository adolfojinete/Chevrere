using Chevrere.SharedKernel.Domain;

namespace Chevrere.Modules.Inventory.Domain;

/// <summary>
/// Immutable ledger entry. Never update after creation; use compensating movements instead.
/// </summary>
public sealed class InventoryMovement : Entity
{
    private InventoryMovement()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid InventoryItemId { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid GlobalProductId { get; private set; }

    public InventoryMovementType Type { get; private set; }

    public long OnHandDelta { get; private set; }

    public long ReservedDelta { get; private set; }

    public long OnHandBefore { get; private set; }

    public long OnHandAfter { get; private set; }

    public long ReservedBefore { get; private set; }

    public long ReservedAfter { get; private set; }

    public string? Reason { get; private set; }

    public string? ReferenceType { get; private set; }

    public Guid? ReferenceId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string CorrelationId { get; private set; } = null!;

    internal static InventoryMovement Create(
        Guid tenantId,
        Guid inventoryItemId,
        Guid storeId,
        Guid globalProductId,
        InventoryMovementType type,
        long onHandBefore,
        long reservedBefore,
        long onHandDelta,
        long reservedDelta,
        string? reason,
        string? referenceType,
        Guid? referenceId,
        Guid? actorUserId,
        string correlationId,
        DateTimeOffset utcNow)
    {
        var onHandAfter = checked(onHandBefore + onHandDelta);
        var reservedAfter = checked(reservedBefore + reservedDelta);
        EnsureBalances(onHandAfter, reservedAfter);

        return new InventoryMovement
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            InventoryItemId = inventoryItemId,
            StoreId = storeId,
            GlobalProductId = globalProductId,
            Type = type,
            OnHandDelta = onHandDelta,
            ReservedDelta = reservedDelta,
            OnHandBefore = onHandBefore,
            OnHandAfter = onHandAfter,
            ReservedBefore = reservedBefore,
            ReservedAfter = reservedAfter,
            Reason = reason,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            OccurredAt = utcNow,
            ActorUserId = actorUserId,
            CorrelationId = correlationId,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    private static void EnsureBalances(long onHand, long reserved)
    {
        if (onHand < 0)
        {
            throw new DomainException("inventory.insufficient_stock", "On-hand stock cannot become negative.");
        }

        if (reserved < 0)
        {
            throw new DomainException("inventory.reserved.invalid", "Reserved stock cannot become negative.");
        }

        if (reserved > onHand)
        {
            throw new DomainException(
                "inventory.reserved.exceeds_on_hand",
                "Reserved stock cannot exceed on-hand stock.");
        }
    }
}
