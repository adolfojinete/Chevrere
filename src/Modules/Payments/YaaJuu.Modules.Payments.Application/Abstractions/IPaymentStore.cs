using YaaJuu.Modules.Payments.Domain;

namespace YaaJuu.Modules.Payments.Application.Abstractions;

public interface IPaymentStore
{
    Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken);

    Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken);

    Task<Payment?> GetByIdForTenantStoreAsync(
        Guid tenantId,
        Guid storeId,
        Guid paymentId,
        CancellationToken cancellationToken);

    Task<PaymentAttempt?> GetAttemptByMerchantReferenceAsync(
        string merchantReference,
        CancellationToken cancellationToken);

    Task<PaymentAttempt?> GetAttemptByProviderTransactionAsync(
        PaymentProvider provider,
        MerchantEnvironment environment,
        string providerTransactionId,
        CancellationToken cancellationToken);

    Task<PaymentMerchantConfiguration?> GetActiveMerchantAsync(
        Guid tenantId,
        PaymentProvider provider,
        CancellationToken cancellationToken);

    Task<PaymentMerchantConfiguration?> GetMerchantByIdAsync(
        Guid merchantConfigurationId,
        CancellationToken cancellationToken);

    Task<PaymentMerchantConfiguration?> GetLatestMerchantAsync(
        Guid tenantId,
        PaymentProvider provider,
        MerchantEnvironment environment,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentMerchantConfiguration>> ListMerchantsForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<PaymentProviderEvent?> GetProviderEventAsync(
        PaymentProvider provider,
        MerchantEnvironment environment,
        string providerEventId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentAttempt>> ListAttemptsForReconciliationAsync(
        int batchSize,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken);

    Task<(IReadOnlyList<Payment> Items, int Total)> ListStorePaymentsAsync(
        Guid tenantId,
        Guid storeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<(IReadOnlyList<Payment> Items, int Total)> ListAdminPaymentsAsync(
        int page,
        int pageSize,
        Guid? tenantId,
        Guid? storeId,
        PaymentStatus? status,
        bool? requiresReconciliation,
        CancellationToken cancellationToken);

    Task<string?> GetOrderNumberAsync(Guid orderId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> GetOrderNumbersAsync(
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken);

    Task<Guid?> GetStoreTenantIdAsync(Guid storeId, CancellationToken cancellationToken);

    void Add(Payment payment);

    void Add(PaymentMerchantConfiguration configuration);

    void Add(PaymentProviderEvent providerEvent);

    void DiscardPendingPayment(Payment payment);

    void DiscardPendingAttempt(PaymentAttempt attempt);

    void DiscardPendingProviderEvent(PaymentProviderEvent providerEvent);

    void DiscardPendingMerchant(PaymentMerchantConfiguration configuration);

    /// <summary>
    /// Detaches a payment aggregate graph from the change tracker after a failed concurrent insert,
    /// without clearing unrelated tracked entities.
    /// </summary>
    void DetachPaymentGraph(Payment payment);

    /// <summary>
    /// After StartAttempt on an existing Payment, ensure only the new attempt remains Added
    /// so historical attempts are not UPDATE'd (xmin false conflicts).
    /// </summary>
    void AcceptHistoricalAttemptsUnchanged(Payment payment, PaymentAttempt newAttempt);

    IDisposable SuspendAutoDetectChanges();
}

public interface IPaymentSecretProtector
{
    string Protect(string plaintext, string purpose);

    string Unprotect(string ciphertext, string purpose);
}

public static class PaymentSecretPurposes
{
    public const string WompiPrivateKey = "payments:wompi:private-key";
    public const string WompiIntegritySecret = "payments:wompi:integrity-secret";
    public const string WompiEventsSecret = "payments:wompi:events-secret";
}
