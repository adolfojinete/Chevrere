using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.Modules.Inventory.Domain;

/// <summary>
/// Holds units of an InventoryItem for a downstream document line (an order item today) until the
/// reservation is released or committed. One reservation per (ReferenceType, ReferenceId).
/// </summary>
public sealed class InventoryReservation : AggregateRoot
{
    private InventoryReservation()
    {
        ReferenceType = null!;
    }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid InventoryItemId { get; private set; }

    public Guid GlobalProductId { get; private set; }

    public string ReferenceType { get; private set; }

    public Guid ReferenceId { get; private set; }

    public long Quantity { get; private set; }

    public InventoryReservationStatus Status { get; private set; }

    public DateTimeOffset? ReleasedAt { get; private set; }

    public DateTimeOffset? CommittedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public static InventoryReservation Open(
        Guid tenantId,
        Guid storeId,
        Guid inventoryItemId,
        Guid globalProductId,
        string referenceType,
        Guid referenceId,
        long quantity,
        DateTimeOffset? expiresAt,
        DateTimeOffset utcNow)
    {
        if (tenantId == Guid.Empty || storeId == Guid.Empty || inventoryItemId == Guid.Empty
            || globalProductId == Guid.Empty || referenceId == Guid.Empty)
        {
            throw new DomainException(
                "inventory.reservation.identity.required",
                "A reservation requires tenant, store, item, product and reference.");
        }

        if (quantity <= 0)
        {
            throw new DomainException(
                "inventory.reservation.quantity.invalid",
                "Reservation quantity must be greater than zero.");
        }

        return new InventoryReservation
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            StoreId = storeId,
            InventoryItemId = inventoryItemId,
            GlobalProductId = globalProductId,
            ReferenceType = Guard.NotNullOrWhiteSpace(referenceType, nameof(referenceType), 64),
            ReferenceId = referenceId,
            Quantity = quantity,
            Status = InventoryReservationStatus.Active,
            ExpiresAt = expiresAt,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    /// <returns>true when the reservation actually moved to Released.</returns>
    public bool Release(DateTimeOffset utcNow)
    {
        if (Status == InventoryReservationStatus.Released)
        {
            return false;
        }

        if (Status == InventoryReservationStatus.Committed)
        {
            throw new DomainException(
                "inventory.reservation.already_committed",
                "A committed reservation cannot be released.");
        }

        Status = InventoryReservationStatus.Released;
        ReleasedAt = utcNow;
        UpdatedAt = utcNow;
        return true;
    }

    /// <returns>true when the reservation actually moved to Committed.</returns>
    public bool Commit(DateTimeOffset utcNow)
    {
        if (Status == InventoryReservationStatus.Committed)
        {
            return false;
        }

        if (Status == InventoryReservationStatus.Released)
        {
            throw new DomainException(
                "inventory.reservation.already_released",
                "A released reservation cannot be committed.");
        }

        Status = InventoryReservationStatus.Committed;
        CommittedAt = utcNow;
        UpdatedAt = utcNow;
        return true;
    }
}
