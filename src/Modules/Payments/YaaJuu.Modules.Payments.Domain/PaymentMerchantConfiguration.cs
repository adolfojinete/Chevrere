using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.Modules.Payments.Domain;

/// <summary>
/// Wompi (or future provider) merchant credentials for a Tenant.
/// Secrets are stored as ciphertext at the infrastructure boundary; Domain only holds opaque protected values.
/// Rotation creates a new configuration and disables the previous active one.
/// </summary>
public sealed class PaymentMerchantConfiguration : AggregateRoot
{
    private PaymentMerchantConfiguration()
    {
        PublicKey = null!;
        EncryptedPrivateKey = null!;
        EncryptedIntegritySecret = null!;
        EncryptedEventsSecret = null!;
    }

    public Guid TenantId { get; private set; }

    public PaymentProvider Provider { get; private set; }

    public MerchantEnvironment Environment { get; private set; }

    public string PublicKey { get; private set; }

    public string EncryptedPrivateKey { get; private set; }

    public string EncryptedIntegritySecret { get; private set; }

    public string EncryptedEventsSecret { get; private set; }

    public bool IsEnabled { get; private set; }

    public int Version { get; private set; }

    public DateTimeOffset? DisabledAt { get; private set; }

    public static PaymentMerchantConfiguration Create(
        Guid tenantId,
        PaymentProvider provider,
        MerchantEnvironment environment,
        string publicKey,
        string encryptedPrivateKey,
        string encryptedIntegritySecret,
        string encryptedEventsSecret,
        int version,
        DateTimeOffset utcNow)
    {
        if (tenantId == Guid.Empty)
        {
            throw new DomainException("payment.merchant.tenant_required", "TenantId is required.");
        }

        if (provider != PaymentProvider.Wompi)
        {
            throw new DomainException("payment.merchant.provider_unsupported", "Only Wompi is supported.");
        }

        if (version < 1)
        {
            throw new DomainException("payment.merchant.version_invalid", "Version must be at least 1.");
        }

        return new PaymentMerchantConfiguration
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Provider = provider,
            Environment = environment,
            PublicKey = Guard.NotNullOrWhiteSpace(publicKey, nameof(publicKey), 200),
            EncryptedPrivateKey = Guard.NotNullOrWhiteSpace(encryptedPrivateKey, nameof(encryptedPrivateKey), 4000),
            EncryptedIntegritySecret = Guard.NotNullOrWhiteSpace(
                encryptedIntegritySecret,
                nameof(encryptedIntegritySecret),
                4000),
            EncryptedEventsSecret = Guard.NotNullOrWhiteSpace(
                encryptedEventsSecret,
                nameof(encryptedEventsSecret),
                4000),
            IsEnabled = false,
            Version = version,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    /// <returns>true when enabled actually flipped.</returns>
    public bool Enable(DateTimeOffset utcNow)
    {
        EnsureSecretsPresent();
        if (IsEnabled)
        {
            return false;
        }

        IsEnabled = true;
        DisabledAt = null;
        UpdatedAt = utcNow;
        return true;
    }

    /// <returns>true when disabled actually flipped.</returns>
    public bool Disable(DateTimeOffset utcNow)
    {
        if (!IsEnabled)
        {
            return false;
        }

        IsEnabled = false;
        DisabledAt = utcNow;
        UpdatedAt = utcNow;
        return true;
    }

    public void ReplaceSecrets(
        string publicKey,
        string encryptedPrivateKey,
        string encryptedIntegritySecret,
        string encryptedEventsSecret,
        DateTimeOffset utcNow)
    {
        PublicKey = Guard.NotNullOrWhiteSpace(publicKey, nameof(publicKey), 200);
        EncryptedPrivateKey = Guard.NotNullOrWhiteSpace(encryptedPrivateKey, nameof(encryptedPrivateKey), 4000);
        EncryptedIntegritySecret = Guard.NotNullOrWhiteSpace(
            encryptedIntegritySecret,
            nameof(encryptedIntegritySecret),
            4000);
        EncryptedEventsSecret = Guard.NotNullOrWhiteSpace(
            encryptedEventsSecret,
            nameof(encryptedEventsSecret),
            4000);
        UpdatedAt = utcNow;
    }

    private void EnsureSecretsPresent()
    {
        if (string.IsNullOrWhiteSpace(PublicKey)
            || string.IsNullOrWhiteSpace(EncryptedPrivateKey)
            || string.IsNullOrWhiteSpace(EncryptedIntegritySecret)
            || string.IsNullOrWhiteSpace(EncryptedEventsSecret))
        {
            throw new DomainException(
                "payment.merchant.incomplete",
                "Merchant configuration is incomplete and cannot be enabled.");
        }
    }
}
