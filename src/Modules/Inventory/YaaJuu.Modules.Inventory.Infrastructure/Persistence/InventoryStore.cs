using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Inventory.Application.Abstractions;
using YaaJuu.Modules.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace YaaJuu.Modules.Inventory.Infrastructure.Persistence;

public sealed class InventoryStoreAccess(YaaJuuDbContext dbContext) : IInventoryStoreAccess
{
    public async Task<Guid?> GetStoreTenantIdAsync(Guid storeId, CancellationToken cancellationToken) =>
        await dbContext.Stores.AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => (Guid?)s.TenantId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> StoreProductExistsAsync(
        Guid tenantId,
        Guid storeId,
        Guid globalProductId,
        CancellationToken cancellationToken) =>
        dbContext.StoreProducts.AsNoTracking().AnyAsync(
            s => s.TenantId == tenantId && s.StoreId == storeId && s.GlobalProductId == globalProductId,
            cancellationToken);
}

public sealed class InventoryStore(YaaJuuDbContext dbContext) : IInventoryStore
{
    public Task<InventoryItem?> GetItemAsync(Guid storeId, Guid globalProductId, CancellationToken cancellationToken) =>
        dbContext.InventoryItems.FirstOrDefaultAsync(
            i => i.StoreId == storeId && i.GlobalProductId == globalProductId,
            cancellationToken);

    public async Task<IReadOnlyList<InventoryItem>> GetItemsByProductsAsync(
        Guid tenantId,
        Guid storeId,
        IReadOnlyList<Guid> globalProductIds,
        CancellationToken cancellationToken)
    {
        if (globalProductIds.Count == 0)
        {
            return [];
        }

        return await dbContext.InventoryItems
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId
                        && i.StoreId == storeId
                        && globalProductIds.Contains(i.GlobalProductId))
            .ToListAsync(cancellationToken);
    }

    public Task<InventoryReservation?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken) =>
        dbContext.InventoryReservations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == reservationId, cancellationToken);

    public async Task<IReadOnlyList<InventoryReservation>> GetReservationsByReferencesAsync(
        Guid tenantId,
        Guid storeId,
        string referenceType,
        IReadOnlyList<Guid> referenceIds,
        CancellationToken cancellationToken)
    {
        if (referenceIds.Count == 0)
        {
            return [];
        }

        return await dbContext.InventoryReservations
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId
                        && r.StoreId == storeId
                        && r.ReferenceType == referenceType
                        && referenceIds.Contains(r.ReferenceId))
            .ToListAsync(cancellationToken);
    }

    public void AddItem(InventoryItem item) => dbContext.InventoryItems.Add(item);

    public void AddMovement(InventoryMovement movement) => dbContext.InventoryMovements.Add(movement);

    public void AddReservation(InventoryReservation reservation) => dbContext.InventoryReservations.Add(reservation);

    public void DiscardItem(InventoryItem item)
    {
        var entry = dbContext.Entry(item);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }

    public void DiscardMovement(InventoryMovement movement)
    {
        var entry = dbContext.Entry(movement);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }

    public void DiscardReservation(InventoryReservation reservation)
    {
        var entry = dbContext.Entry(reservation);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }

    public Task<InventoryMovement?> GetMovementAsync(Guid movementId, CancellationToken cancellationToken) =>
        dbContext.InventoryMovements.AsNoTracking().FirstOrDefaultAsync(m => m.Id == movementId, cancellationToken);

    public async Task<(IReadOnlyList<InventoryListProjection> Items, int Total)> ListStoreInventoryAsync(
        Guid storeId,
        int page,
        int pageSize,
        string? search,
        bool? initialized,
        bool? enabled,
        string? sortBy,
        CancellationToken cancellationToken)
    {
        var query =
            from offering in dbContext.StoreProducts.AsNoTracking()
            join product in dbContext.GlobalProducts.AsNoTracking() on offering.GlobalProductId equals product.Id
            join item in dbContext.InventoryItems.AsNoTracking()
                on new { offering.StoreId, offering.GlobalProductId }
                equals new { item.StoreId, item.GlobalProductId }
                into items
            from item in items.DefaultIfEmpty()
            where offering.StoreId == storeId
            select new { offering, product, item };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.product.Sku, term) || EF.Functions.ILike(x.product.Name, term));
        }

        if (initialized is true)
        {
            query = query.Where(x => x.item != null);
        }
        else if (initialized is false)
        {
            query = query.Where(x => x.item == null);
        }

        if (enabled is { } enabledValue)
        {
            query = query.Where(x => x.offering.IsEnabled == enabledValue);
        }

        var total = await query.CountAsync(cancellationToken);

        query = (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "sku" => query.OrderBy(x => x.product.Sku),
            "available" => query.OrderBy(x => (x.item == null ? 0 : x.item.OnHand - x.item.Reserved)),
            _ => query.OrderBy(x => x.product.Name)
        };

        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new InventoryListProjection(
                x.offering.StoreId,
                x.offering.GlobalProductId,
                x.product.Sku,
                x.product.Name,
                x.offering.IsEnabled,
                x.item == null ? null : x.item.Id,
                x.item == null ? null : x.item.OnHand,
                x.item == null ? null : x.item.Reserved))
            .ToListAsync(cancellationToken);

        return (rows, total);
    }

    public async Task<InventoryListProjection?> GetStoreProductProjectionAsync(
        Guid storeId,
        Guid globalProductId,
        CancellationToken cancellationToken)
    {
        return await (
            from offering in dbContext.StoreProducts.AsNoTracking()
            join product in dbContext.GlobalProducts.AsNoTracking() on offering.GlobalProductId equals product.Id
            join item in dbContext.InventoryItems.AsNoTracking()
                on new { offering.StoreId, offering.GlobalProductId }
                equals new { item.StoreId, item.GlobalProductId }
                into items
            from item in items.DefaultIfEmpty()
            where offering.StoreId == storeId && offering.GlobalProductId == globalProductId
            select new InventoryListProjection(
                offering.StoreId,
                offering.GlobalProductId,
                product.Sku,
                product.Name,
                offering.IsEnabled,
                item == null ? null : item.Id,
                item == null ? null : item.OnHand,
                item == null ? null : item.Reserved)
        ).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<InventoryMovement> Items, int Total)> ListMovementsAsync(
        Guid inventoryItemId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.InventoryMovements.AsNoTracking()
            .Where(m => m.InventoryItemId == inventoryItemId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(m => m.OccurredAt)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }
}
