namespace Chevrere.Modules.Inventory.Domain;

public enum InventoryMovementType
{
    InitialStock = 1,
    AdjustmentIncrease = 2,
    AdjustmentDecrease = 3,
    Waste = 4,
    Receipt = 5,
    Reservation = 6,
    ReservationReleased = 7,
    ReservationCommitted = 8
}
