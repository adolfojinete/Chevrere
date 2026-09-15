namespace YaaJuu.SharedKernel.Inventory;

/// <summary>
/// Reservation batch failures that are data-integrity violations, not ordinary stock conflicts.
/// </summary>
public static class InventoryReservationErrors
{
    public const string ReferenceDuplicate = "inventory.reservation.reference.duplicate";

    public const string IncompleteSet = "inventory.reservation.incomplete_set";

    public const string IntegrityError = "inventory.reservation.integrity_error";

    public const string InvalidState = "inventory.reservation.invalid_state";

    public const string NotInitialized = "inventory.not_initialized";

    public const string AlreadyCommitted = "inventory.reservation.already_committed";

    public const string AlreadyReleased = "inventory.reservation.already_released";

    public static bool IsIntegrityFailure(string? code) =>
        code is ReferenceDuplicate
            or IncompleteSet
            or IntegrityError
            or InvalidState
            or NotInitialized
            or AlreadyCommitted
            or AlreadyReleased;
}
