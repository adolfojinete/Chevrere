using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Payments.Application.Abstractions;
using YaaJuu.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;

namespace YaaJuu.Modules.Payments.Infrastructure.Persistence;

public sealed class PaymentStore(YaaJuuDbContext dbContext) : IPaymentStore
{
    public async Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var payment = await dbContext.Payments
            .IgnoreQueryFilters()
            .Include(p => p.Attempts)
            .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
        return payment;
    }

    public async Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken) =>
        await dbContext.Payments
            .IgnoreQueryFilters()
            .Include(p => p.Attempts)
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);

    public async Task<Payment?> GetByIdForTenantStoreAsync(
        Guid tenantId,
        Guid storeId,
        Guid paymentId,
        CancellationToken cancellationToken) =>
        await dbContext.Payments
            .IgnoreQueryFilters()
            .Include(p => p.Attempts)
            .FirstOrDefaultAsync(
                p => p.Id == paymentId && p.TenantId == tenantId && p.StoreId == storeId,
                cancellationToken);

    public Task<PaymentAttempt?> GetAttemptByMerchantReferenceAsync(
        string merchantReference,
        CancellationToken cancellationToken) =>
        dbContext.PaymentAttempts
            .FirstOrDefaultAsync(a => a.MerchantReference == merchantReference, cancellationToken);

    public Task<PaymentAttempt?> GetAttemptByProviderTransactionAsync(
        PaymentProvider provider,
        MerchantEnvironment environment,
        string providerTransactionId,
        CancellationToken cancellationToken) =>
        dbContext.PaymentAttempts.FirstOrDefaultAsync(
            a => a.Provider == provider
                 && a.Environment == environment
                 && a.ProviderTransactionId == providerTransactionId,
            cancellationToken);

    public Task<PaymentMerchantConfiguration?> GetActiveMerchantAsync(
        Guid tenantId,
        PaymentProvider provider,
        CancellationToken cancellationToken) =>
        dbContext.PaymentMerchantConfigurations
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.Provider == provider && c.IsEnabled)
            .OrderByDescending(c => c.Version)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PaymentMerchantConfiguration?> GetMerchantByIdAsync(
        Guid merchantConfigurationId,
        CancellationToken cancellationToken) =>
        dbContext.PaymentMerchantConfigurations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == merchantConfigurationId, cancellationToken);

    public Task<PaymentMerchantConfiguration?> GetLatestMerchantAsync(
        Guid tenantId,
        PaymentProvider provider,
        MerchantEnvironment environment,
        CancellationToken cancellationToken) =>
        dbContext.PaymentMerchantConfigurations
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.Provider == provider && c.Environment == environment)
            .OrderByDescending(c => c.Version)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PaymentMerchantConfiguration>> ListMerchantsForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await dbContext.PaymentMerchantConfigurations
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.Version)
            .ToListAsync(cancellationToken);

    public Task<PaymentProviderEvent?> GetProviderEventAsync(
        PaymentProvider provider,
        MerchantEnvironment environment,
        string providerEventId,
        CancellationToken cancellationToken) =>
        dbContext.PaymentProviderEvents.FirstOrDefaultAsync(
            e => e.Provider == provider
                 && e.Environment == environment
                 && e.ProviderEventId == providerEventId,
            cancellationToken);

    public async Task<IReadOnlyList<PaymentAttempt>> ListAttemptsForReconciliationAsync(
        int batchSize,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken) =>
        await dbContext.PaymentAttempts
            .Where(a =>
                (a.Status == PaymentAttemptStatus.Pending
                 || a.Status == PaymentAttemptStatus.Unknown
                 || a.Status == PaymentAttemptStatus.Initiating)
                && a.CreatedAt <= olderThan
                && a.ProviderTransactionId != null)
            .OrderBy(a => a.LastProviderCheckAt ?? a.CreatedAt)
            .ThenBy(a => a.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Payment> Items, int Total)> ListStorePaymentsAsync(
        Guid tenantId,
        Guid storeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Payments.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.StoreId == storeId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(p => p.Attempts)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<(IReadOnlyList<Payment> Items, int Total)> ListAdminPaymentsAsync(
        int page,
        int pageSize,
        Guid? tenantId,
        Guid? storeId,
        PaymentStatus? status,
        bool? requiresReconciliation,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Payments.IgnoreQueryFilters().AsNoTracking().AsQueryable();
        if (tenantId is not null)
        {
            query = query.Where(p => p.TenantId == tenantId);
        }

        if (storeId is not null)
        {
            query = query.Where(p => p.StoreId == storeId);
        }

        if (status is not null)
        {
            query = query.Where(p => p.Status == status);
        }

        if (requiresReconciliation is not null)
        {
            query = query.Where(p => p.RequiresReconciliation == requiresReconciliation);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(p => p.Attempts)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<string?> GetOrderNumberAsync(Guid orderId, CancellationToken cancellationToken) =>
        dbContext.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => (string?)o.Number)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetOrderNumbersAsync(
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await dbContext.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Number, cancellationToken);
    }

    public Task<Guid?> GetStoreTenantIdAsync(Guid storeId, CancellationToken cancellationToken) =>
        dbContext.Stores.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => (Guid?)s.TenantId)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(Payment payment) => dbContext.Payments.Add(payment);

    public void Add(PaymentMerchantConfiguration configuration) =>
        dbContext.PaymentMerchantConfigurations.Add(configuration);

    public void Add(PaymentProviderEvent providerEvent) =>
        dbContext.PaymentProviderEvents.Add(providerEvent);

    public void DiscardPendingPayment(Payment payment)
    {
        foreach (var attempt in payment.Attempts.ToList())
        {
            DetachAdded(attempt);
        }

        DetachAdded(payment);
    }

    public void DiscardPendingAttempt(PaymentAttempt attempt) => DetachAdded(attempt);

    public void DiscardPendingProviderEvent(PaymentProviderEvent providerEvent) => DetachAdded(providerEvent);

    public void DiscardPendingMerchant(PaymentMerchantConfiguration configuration) => DetachAdded(configuration);

    public void DetachPaymentGraph(Payment payment)
    {
        foreach (var attempt in payment.Attempts.ToList())
        {
            var attemptEntry = dbContext.Entry(attempt);
            if (attemptEntry.State != EntityState.Detached)
            {
                attemptEntry.State = EntityState.Detached;
            }
        }

        var paymentEntry = dbContext.Entry(payment);
        if (paymentEntry.State != EntityState.Detached)
        {
            paymentEntry.State = EntityState.Detached;
        }
    }

    public void AcceptHistoricalAttemptsUnchanged(Payment payment, PaymentAttempt newAttempt)
    {
        foreach (var existing in payment.Attempts)
        {
            if (existing.Id == newAttempt.Id)
            {
                continue;
            }

            var entry = dbContext.Entry(existing);
            if (entry.State == EntityState.Detached)
            {
                continue;
            }

            entry.State = EntityState.Unchanged;
            foreach (var property in entry.Properties)
            {
                property.IsModified = false;
            }
        }

        var paymentEntry = dbContext.Entry(payment);
        if (paymentEntry.State is EntityState.Modified or EntityState.Unchanged)
        {
            paymentEntry.State = EntityState.Unchanged;
            foreach (var property in paymentEntry.Properties)
            {
                property.IsModified = false;
            }
        }

        var newEntry = dbContext.Entry(newAttempt);
        if (newEntry.State != EntityState.Added)
        {
            newEntry.State = EntityState.Added;
        }
    }

    public IDisposable SuspendAutoDetectChanges()
    {
        var previous = dbContext.ChangeTracker.AutoDetectChangesEnabled;
        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        return new ResumeDetectChanges(dbContext, previous);
    }

    private sealed class ResumeDetectChanges(YaaJuuDbContext dbContext, bool previous) : IDisposable
    {
        public void Dispose() => dbContext.ChangeTracker.AutoDetectChangesEnabled = previous;
    }

    private void DetachAdded<T>(T entity) where T : class
    {
        var entry = dbContext.Entry(entity);
        if (entry.State == EntityState.Added)
        {
            entry.State = EntityState.Detached;
        }
    }
}
