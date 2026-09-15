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

    public void Add(Payment payment) => dbContext.Payments.Add(payment);

    public void Add(PaymentMerchantConfiguration configuration) =>
        dbContext.PaymentMerchantConfigurations.Add(configuration);

    public void Add(PaymentProviderEvent providerEvent) =>
        dbContext.PaymentProviderEvents.Add(providerEvent);

    public void DiscardPendingPayment(Payment payment) => DetachAdded(payment);

    public void DiscardPendingAttempt(PaymentAttempt attempt) => DetachAdded(attempt);

    public void DiscardPendingProviderEvent(PaymentProviderEvent providerEvent) => DetachAdded(providerEvent);

    public void DiscardPendingMerchant(PaymentMerchantConfiguration configuration) => DetachAdded(configuration);

    private void DetachAdded<T>(T entity) where T : class
    {
        var entry = dbContext.Entry(entity);
        if (entry.State == EntityState.Added)
        {
            entry.State = EntityState.Detached;
        }
    }
}
