using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Catalog.Domain;
using YaaJuu.Modules.Procurement.Application.Abstractions;
using YaaJuu.Modules.Procurement.Domain;
using Microsoft.EntityFrameworkCore;

namespace YaaJuu.Modules.Procurement.Infrastructure.Persistence;

public sealed class ProcurementStoreAccess(YaaJuuDbContext dbContext) : IProcurementStoreAccess
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

    public Task<bool> GlobalProductIsActiveAsync(Guid globalProductId, CancellationToken cancellationToken) =>
        dbContext.GlobalProducts.AsNoTracking().AnyAsync(
            p => p.Id == globalProductId && p.Status == GlobalProductStatus.Active,
            cancellationToken);
}

public sealed class ProcurementStore(YaaJuuDbContext dbContext) : IProcurementStore
{
    public Task<Supplier?> GetSupplierAsync(Guid supplierId, CancellationToken cancellationToken) =>
        dbContext.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken);

    public Task<bool> SupplierCodeExistsAsync(Guid tenantId, string code, CancellationToken cancellationToken) =>
        dbContext.Suppliers.AsNoTracking()
            .AnyAsync(s => s.TenantId == tenantId && s.Code == code, cancellationToken);

    public void AddSupplier(Supplier supplier) => dbContext.Suppliers.Add(supplier);

    public async Task<(IReadOnlyList<Supplier> Items, int Total)> ListSuppliersAsync(
        Guid tenantId,
        int page,
        int pageSize,
        string? search,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Suppliers.AsNoTracking().Where(s => s.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(s => EF.Functions.ILike(s.Code, term) || EF.Functions.ILike(s.Name, term));
        }

        if (isActive is { } activeValue)
        {
            query = query.Where(s => s.IsActive == activeValue);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public void AddPurchaseOrder(PurchaseOrder order) => dbContext.PurchaseOrders.Add(order);

    public Task<PurchaseOrder?> GetPurchaseOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken) =>
        dbContext.PurchaseOrders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == purchaseOrderId, cancellationToken);

    public Task<PurchaseOrder?> ReadPurchaseOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken) =>
        dbContext.PurchaseOrders.AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == purchaseOrderId, cancellationToken);

    public async Task<(IReadOnlyList<PurchaseOrderProjection> Items, int Total)> ListPurchaseOrdersAsync(
        Guid tenantId,
        Guid storeId,
        int page,
        int pageSize,
        PurchaseOrderStatus? status,
        Guid? supplierId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PurchaseOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.StoreId == storeId);

        if (status is { } statusValue)
        {
            query = query.Where(o => o.Status == statusValue);
        }

        if (supplierId is { } supplier)
        {
            query = query.Where(o => o.SupplierId == supplier);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new PurchaseOrderProjection(
                o.Id,
                o.StoreId,
                o.SupplierId,
                o.Number,
                o.Status,
                o.Items.Count,
                o.Items.Sum(i => i.OrderedQuantity),
                o.Items.Sum(i => i.ReceivedQuantity),
                o.CreatedAt,
                o.UpdatedAt))
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public void AddGoodsReceipt(GoodsReceipt receipt) => dbContext.GoodsReceipts.Add(receipt);

    public Task<GoodsReceipt?> ReadGoodsReceiptAsync(Guid goodsReceiptId, CancellationToken cancellationToken) =>
        dbContext.GoodsReceipts.AsNoTracking()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == goodsReceiptId, cancellationToken);

    public async Task<(IReadOnlyList<GoodsReceiptProjection> Items, int Total)> ListGoodsReceiptsAsync(
        Guid tenantId,
        Guid purchaseOrderId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.GoodsReceipts.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.PurchaseOrderId == purchaseOrderId);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.ReceivedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new GoodsReceiptProjection(
                r.Id,
                r.PurchaseOrderId,
                r.ReceiptNumber,
                r.ReceivedAt,
                r.PurchaseOrderStatusAfter,
                r.Items.Count,
                r.Items.Sum(i => i.ReceivedQuantity)))
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    /// <remarks>
    /// Detaching a child makes EF fix up the parent navigation, so the lines are copied out before
    /// being walked.
    /// </remarks>
    public void DiscardPurchaseOrder(PurchaseOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        foreach (var item in order.Items.ToList())
        {
            Detach(item);
        }

        Detach(order);
    }

    /// <inheritdoc cref="DiscardPurchaseOrder"/>
    public void DiscardGoodsReceipt(GoodsReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        foreach (var item in receipt.Items.ToList())
        {
            Detach(item);
        }

        Detach(receipt);
    }

    private void Detach(object entity)
    {
        var entry = dbContext.Entry(entity);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }
}
