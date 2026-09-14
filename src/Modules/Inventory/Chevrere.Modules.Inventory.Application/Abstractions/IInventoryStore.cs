using Chevrere.Modules.Inventory.Domain;

namespace Chevrere.Modules.Inventory.Application.Abstractions;

public interface IInventoryStoreAccess
{
    Task<Guid?> GetStoreTenantIdAsync(Guid storeId, CancellationToken cancellationToken);

    Task<bool> StoreProductExistsAsync(
        Guid tenantId,
        Guid storeId,
        Guid globalProductId,
        CancellationToken cancellationToken);
}

public interface IInventoryStore
{
    Task<InventoryItem?> GetItemAsync(Guid storeId, Guid globalProductId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryItem>> GetItemsByProductsAsync(
        Guid tenantId,
        Guid storeId,
        IReadOnlyList<Guid> globalProductIds,
        CancellationToken cancellationToken);

    Task<InventoryReservation?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryReservation>> GetReservationsByReferencesAsync(
        Guid tenantId,
        Guid storeId,
        string referenceType,
        IReadOnlyList<Guid> referenceIds,
        CancellationToken cancellationToken);

    void AddItem(InventoryItem item);

    void AddMovement(InventoryMovement movement);

    void AddReservation(InventoryReservation reservation);

    void DiscardItem(InventoryItem item);

    void DiscardMovement(InventoryMovement movement);

    void DiscardReservation(InventoryReservation reservation);

    Task<InventoryMovement?> GetMovementAsync(Guid movementId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<InventoryListProjection> Items, int Total)> ListStoreInventoryAsync(
        Guid storeId,
        int page,
        int pageSize,
        string? search,
        bool? initialized,
        bool? enabled,
        string? sortBy,
        CancellationToken cancellationToken);

    Task<InventoryListProjection?> GetStoreProductProjectionAsync(
        Guid storeId,
        Guid globalProductId,
        CancellationToken cancellationToken);

    Task<(IReadOnlyList<InventoryMovement> Items, int Total)> ListMovementsAsync(
        Guid inventoryItemId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}

public sealed record InventoryListProjection(
    Guid StoreId,
    Guid GlobalProductId,
    string Sku,
    string Name,
    bool IsStoreProductEnabled,
    Guid? InventoryItemId,
    long? OnHand,
    long? Reserved);
