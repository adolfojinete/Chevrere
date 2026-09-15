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

    void Add(Payment payment);

    void Add(PaymentMerchantConfiguration configuration);

    void Add(PaymentProviderEvent providerEvent);

    void DiscardPendingPayment(Payment payment);

    void DiscardPendingAttempt(PaymentAttempt attempt);

    void DiscardPendingProviderEvent(PaymentProviderEvent providerEvent);

    void DiscardPendingMerchant(PaymentMerchantConfiguration configuration);
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
