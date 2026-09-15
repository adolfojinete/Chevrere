using YaaJuu.SharedKernel.Domain;
using YaaJuu.SharedKernel.Domain.ValueObjects;

namespace YaaJuu.Modules.Payments.Domain;

public sealed class PaymentAttempt : Entity
{
    private PaymentAttempt()
    {
        MerchantReference = null!;
        Currency = null!;
        ProviderRawStatus = null!;
    }

    public Guid PaymentId { get; private set; }

    public Guid MerchantConfigurationId { get; private set; }

    public PaymentProvider Provider { get; private set; }

    public MerchantEnvironment Environment { get; private set; }

    public PaymentAttemptStatus Status { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public string MerchantReference { get; private set; }

    public string? ProviderTransactionId { get; private set; }

    public string? ProviderRawStatus { get; private set; }

    public string? FailureCode { get; private set; }

    public string? FailureMessage { get; private set; }

    public string? CheckoutActionJson { get; private set; }

    public DateTimeOffset? InitiationStartedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset? DeclinedAt { get; private set; }

    public DateTimeOffset? LastProviderCheckAt { get; private set; }

    public DateTimeOffset? ProviderOccurredAt { get; private set; }

    internal static PaymentAttempt Create(
        Guid paymentId,
        Guid merchantConfigurationId,
        PaymentProvider provider,
        MerchantEnvironment environment,
        Money amount,
        string merchantReference,
        DateTimeOffset utcNow)
    {
        return new PaymentAttempt
        {
            Id = Guid.CreateVersion7(),
            PaymentId = paymentId,
            MerchantConfigurationId = merchantConfigurationId,
            Provider = provider,
            Environment = environment,
            Status = PaymentAttemptStatus.Created,
            Amount = amount.Amount,
            Currency = amount.Currency,
            MerchantReference = Guard.NotNullOrWhiteSpace(merchantReference, nameof(merchantReference), 255),
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    /// <returns>true when claim succeeded.</returns>
    public bool TryClaimInitiation(DateTimeOffset utcNow)
    {
        if (Status != PaymentAttemptStatus.Created)
        {
            return false;
        }

        Status = PaymentAttemptStatus.Initiating;
        InitiationStartedAt = utcNow;
        UpdatedAt = utcNow;
        return true;
    }

    public bool MarkPending(string? providerTransactionId, string? rawStatus, string? checkoutActionJson, DateTimeOffset utcNow)
    {
        if (Status is PaymentAttemptStatus.Approved or PaymentAttemptStatus.Declined or PaymentAttemptStatus.Voided)
        {
            return false;
        }

        if (Status == PaymentAttemptStatus.Pending
            && ProviderTransactionId == providerTransactionId
            && ProviderRawStatus == Sanitize(rawStatus, 64))
        {
            return false;
        }

        AttachProviderTransaction(providerTransactionId);
        Status = PaymentAttemptStatus.Pending;
        ProviderRawStatus = Sanitize(rawStatus, 64);
        if (checkoutActionJson is not null)
        {
            CheckoutActionJson = Guard.NotNullOrWhiteSpace(checkoutActionJson, nameof(checkoutActionJson), 8000);
        }

        UpdatedAt = utcNow;
        return true;
    }

    public bool MarkUnknown(string? providerTransactionId, string? rawStatus, DateTimeOffset utcNow)
    {
        if (Status is PaymentAttemptStatus.Approved or PaymentAttemptStatus.Declined or PaymentAttemptStatus.Voided)
        {
            return false;
        }

        if (Status == PaymentAttemptStatus.Unknown
            && ProviderTransactionId == providerTransactionId)
        {
            return false;
        }

        AttachProviderTransaction(providerTransactionId);
        Status = PaymentAttemptStatus.Unknown;
        ProviderRawStatus = Sanitize(rawStatus, 64);
        UpdatedAt = utcNow;
        return true;
    }

    public bool MarkError(string? failureCode, string? failureMessage, DateTimeOffset utcNow)
    {
        if (Status is PaymentAttemptStatus.Approved)
        {
            return false;
        }

        if (Status == PaymentAttemptStatus.Error)
        {
            return false;
        }

        Status = PaymentAttemptStatus.Error;
        FailureCode = Sanitize(failureCode, 64);
        FailureMessage = Sanitize(failureMessage, 500);
        DeclinedAt ??= utcNow;
        UpdatedAt = utcNow;
        return true;
    }

    /// <returns>true when mutated to Approved.</returns>
    public bool MarkApproved(
        string providerTransactionId,
        string? rawStatus,
        DateTimeOffset utcNow,
        DateTimeOffset? providerOccurredAt)
    {
        if (Status == PaymentAttemptStatus.Approved)
        {
            AttachProviderTransaction(providerTransactionId);
            return false;
        }

        AttachProviderTransaction(providerTransactionId);
        Status = PaymentAttemptStatus.Approved;
        ProviderRawStatus = Sanitize(rawStatus, 64);
        ApprovedAt = utcNow;
        ProviderOccurredAt = providerOccurredAt;
        UpdatedAt = utcNow;
        return true;
    }

    /// <returns>true when mutated to Declined.</returns>
    public bool MarkDeclined(
        string? providerTransactionId,
        string? rawStatus,
        string? failureCode,
        string? failureMessage,
        DateTimeOffset utcNow,
        DateTimeOffset? providerOccurredAt)
    {
        if (Status == PaymentAttemptStatus.Approved)
        {
            return false;
        }

        if (Status == PaymentAttemptStatus.Declined)
        {
            return false;
        }

        AttachProviderTransaction(providerTransactionId);
        Status = PaymentAttemptStatus.Declined;
        ProviderRawStatus = Sanitize(rawStatus, 64);
        FailureCode = Sanitize(failureCode, 64);
        FailureMessage = Sanitize(failureMessage, 500);
        DeclinedAt = utcNow;
        ProviderOccurredAt = providerOccurredAt;
        UpdatedAt = utcNow;
        return true;
    }

    public bool MarkVoided(
        string? providerTransactionId,
        string? rawStatus,
        DateTimeOffset utcNow,
        DateTimeOffset? providerOccurredAt)
    {
        if (Status == PaymentAttemptStatus.Approved)
        {
            return false;
        }

        if (Status == PaymentAttemptStatus.Voided)
        {
            return false;
        }

        AttachProviderTransaction(providerTransactionId);
        Status = PaymentAttemptStatus.Voided;
        ProviderRawStatus = Sanitize(rawStatus, 64);
        DeclinedAt ??= utcNow;
        ProviderOccurredAt = providerOccurredAt;
        UpdatedAt = utcNow;
        return true;
    }

    public void RecordProviderCheck(DateTimeOffset utcNow)
    {
        LastProviderCheckAt = utcNow;
        UpdatedAt = utcNow;
    }

    public bool IsInFlight => PaymentAttemptStatusRules.IsInFlight(Status);

    private void AttachProviderTransaction(string? providerTransactionId)
    {
        if (string.IsNullOrWhiteSpace(providerTransactionId))
        {
            return;
        }

        var normalized = Guard.NotNullOrWhiteSpace(providerTransactionId, nameof(providerTransactionId), 128);
        if (ProviderTransactionId is null)
        {
            ProviderTransactionId = normalized;
            return;
        }

        if (!string.Equals(ProviderTransactionId, normalized, StringComparison.Ordinal))
        {
            throw new DomainException(
                "payment.attempt.provider_transaction_mismatch",
                "Provider transaction identity cannot change once assigned.");
        }
    }

    private static string? Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
