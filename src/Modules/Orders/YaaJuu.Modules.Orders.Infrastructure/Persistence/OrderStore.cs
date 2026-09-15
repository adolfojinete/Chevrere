using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Orders.Application.Abstractions;
using YaaJuu.Modules.Orders.Domain;
using Microsoft.EntityFrameworkCore;

namespace YaaJuu.Modules.Orders.Infrastructure.Persistence;

public sealed class OrderStoreAccess(YaaJuuDbContext dbContext) : IOrderStoreAccess
{
    public async Task<Guid?> GetStoreTenantIdAsync(Guid storeId, CancellationToken cancellationToken) =>
        await dbContext.Stores.AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => (Guid?)s.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
}

public sealed class OrderStore(YaaJuuDbContext dbContext) : IOrderStore
{
    private readonly Dictionary<Guid, uint> _loadedCartXmin = [];
    private readonly HashSet<Guid> _loadedCartItemIds = [];

    public async Task<Cart?> GetActiveCartAsync(Guid consumerUserId, CancellationToken cancellationToken)
    {
        var cart = await dbContext.Carts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c => c.ConsumerUserId == consumerUserId && c.Status == CartStatus.Active,
                cancellationToken);
        CaptureCartXmin(cart);
        await LoadCartItemsAsync(cart, cancellationToken);
        return cart;
    }

    public async Task<Cart?> GetCartAsync(Guid cartId, CancellationToken cancellationToken)
    {
        var cart = await dbContext.Carts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == cartId, cancellationToken);
        CaptureCartXmin(cart);
        await LoadCartItemsAsync(cart, cancellationToken);
        return cart;
    }

    private async Task LoadCartItemsAsync(Cart? cart, CancellationToken cancellationToken)
    {
        if (cart is null)
        {
            return;
        }

        await dbContext.CartItems
            .IgnoreQueryFilters()
            .Where(i => i.CartId == cart.Id)
            .LoadAsync(cancellationToken);

        foreach (var item in cart.Items)
        {
            _loadedCartItemIds.Add(item.Id);
        }
    }

    private void CaptureCartXmin(Cart? cart)
    {
        if (cart is null)
        {
            return;
        }

        var xmin = dbContext.Entry(cart).Property<uint>("xmin").OriginalValue;
        if (xmin != 0)
        {
            _loadedCartXmin[cart.Id] = xmin;
        }
    }

    public void AddCart(Cart cart) => dbContext.Carts.Add(cart);

    public uint GetCartVersion(Cart cart)
    {
        if (_loadedCartXmin.TryGetValue(cart.Id, out var captured) && captured != 0)
        {
            return captured;
        }

        return dbContext.Entry(cart).Property<uint>("xmin").CurrentValue;
    }

    public void PrepareCartForSave(Cart cart)
    {
        ArgumentNullException.ThrowIfNull(cart);
        RestoreCartConcurrencyToken(cart);

        var currentIds = cart.Items.Select(i => i.Id).ToHashSet();
        foreach (var item in cart.Items)
        {
            var itemEntry = dbContext.Entry(item);
            if (itemEntry.State == EntityState.Detached)
            {
                dbContext.CartItems.Add(item);
            }
            else if (itemEntry.State == EntityState.Modified && !_loadedCartItemIds.Contains(item.Id))
            {
                itemEntry.State = EntityState.Added;
            }
        }

        foreach (var itemEntry in dbContext.ChangeTracker.Entries<CartItem>()
            .Where(e => e.Entity.CartId == cart.Id && !currentIds.Contains(e.Entity.Id))
            .ToList())
        {
            itemEntry.State = itemEntry.State == EntityState.Added
                ? EntityState.Detached
                : EntityState.Deleted;
        }
    }

    private void RestoreCartConcurrencyToken(Cart cart)
    {
        var entry = dbContext.Entry(cart);
        if (entry.State is EntityState.Added or EntityState.Detached)
        {
            return;
        }

        if (!_loadedCartXmin.TryGetValue(cart.Id, out var xmin) || xmin == 0)
        {
            return;
        }

        var token = entry.Property<uint>("xmin");
        token.OriginalValue = xmin;
        token.CurrentValue = xmin;
        token.IsModified = false;
    }

    public Task<Order?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        dbContext.Orders
            .IgnoreQueryFilters()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    public Task<Order?> GetOrderForConsumerAsync(
        Guid orderId,
        Guid consumerUserId,
        CancellationToken cancellationToken) =>
        dbContext.Orders
            .IgnoreQueryFilters()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.ConsumerUserId == consumerUserId, cancellationToken);

    public Task<Order?> GetOrderForStoreAsync(
        Guid tenantId,
        Guid storeId,
        Guid orderId,
        CancellationToken cancellationToken) =>
        dbContext.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(
                o => o.Id == orderId && o.TenantId == tenantId && o.StoreId == storeId,
                cancellationToken);

    public Task<Order?> ReadOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        dbContext.Orders.AsNoTracking()
            .IgnoreQueryFilters()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    public void AddOrder(Order order) => dbContext.Orders.Add(order);

    public async Task<(IReadOnlyList<Order> Items, int Total)> ListConsumerOrdersAsync(
        Guid consumerUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Orders.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(o => o.ConsumerUserId == consumerUserId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<(IReadOnlyList<Order> Items, int Total)> ListStoreOrdersAsync(
        Guid tenantId,
        Guid storeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Orders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.StoreId == storeId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<Guid>> ListExpiredPendingOrderIdsAsync(
        DateTimeOffset utcNow,
        int batchSize,
        CancellationToken cancellationToken) =>
        await dbContext.Orders.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(o => o.Status == OrderStatus.PendingPayment && o.ExpiresAt <= utcNow)
            .OrderBy(o => o.ExpiresAt)
            .ThenBy(o => o.Id)
            .Take(batchSize)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

    public void DiscardCart(Cart cart)
    {
        foreach (var item in cart.Items.ToList())
        {
            Detach(item);
        }

        Detach(cart);
    }

    public void DiscardOrder(Order order)
    {
        foreach (var item in order.Items.ToList())
        {
            Detach(item);
        }

        Detach(order);
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
