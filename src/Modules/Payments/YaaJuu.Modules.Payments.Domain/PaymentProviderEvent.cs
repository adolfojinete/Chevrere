using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.Modules.Payments.Domain;

/// <summary>
/// Authenticated evidence that a provider notified YaaJuu about an event.
/// Not trusted until signature verification succeeds at the application boundary.
/// </summary>
public sealed class PaymentProviderEvent : Entity
{
    private PaymentProviderEvent()
    {
        ProviderEventId = null!;
        EventType = null!;
        PayloadHash = null!;
    }

    public PaymentProvider Provider { get; private set; }

    public MerchantEnvironment Environment { get; private set; }

    public string ProviderEventId { get; private set; }

    public string EventType { get; private set; }

    public string PayloadHash { get; private set; }

    public Guid? PaymentAttemptId { get; private set; }

    public Guid? MerchantConfigurationId { get; private set; }

    public PaymentProviderEventStatus Status { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public DateTimeOffset? ProviderOccurredAt { get; private set; }

    public static PaymentProviderEvent CreateTrusted(
        PaymentProvider provider,
        MerchantEnvironment environment,
        string providerEventId,
        string eventType,
        string payloadHash,
        Guid paymentAttemptId,
        Guid merchantConfigurationId,
        DateTimeOffset receivedAt,
        DateTimeOffset? providerOccurredAt)
    {
        return new PaymentProviderEvent
        {
            Id = Guid.CreateVersion7(),
            Provider = provider,
            Environment = environment,
            ProviderEventId = Guard.NotNullOrWhiteSpace(providerEventId, nameof(providerEventId), 128),
            EventType = Guard.NotNullOrWhiteSpace(eventType, nameof(eventType), 100),
            PayloadHash = Guard.NotNullOrWhiteSpace(payloadHash, nameof(payloadHash), 128),
            PaymentAttemptId = paymentAttemptId,
            MerchantConfigurationId = merchantConfigurationId,
            Status = PaymentProviderEventStatus.Received,
            ReceivedAt = receivedAt,
            ProviderOccurredAt = providerOccurredAt,
            CreatedAt = receivedAt,
            UpdatedAt = receivedAt
        };
    }

    public bool MarkProcessed(DateTimeOffset utcNow)
    {
        if (Status is PaymentProviderEventStatus.Processed or PaymentProviderEventStatus.IgnoredDuplicate)
        {
            return false;
        }

        Status = PaymentProviderEventStatus.Processed;
        ProcessedAt = utcNow;
        UpdatedAt = utcNow;
        return true;
    }

    public bool MarkIgnoredDuplicate(DateTimeOffset utcNow)
    {
        if (Status == PaymentProviderEventStatus.IgnoredDuplicate)
        {
            return false;
        }

        Status = PaymentProviderEventStatus.IgnoredDuplicate;
        ProcessedAt = utcNow;
        UpdatedAt = utcNow;
        return true;
    }

    public bool MarkAnomaly(string reason, DateTimeOffset utcNow)
    {
        Status = PaymentProviderEventStatus.Anomaly;
        FailureReason = Guard.NotNullOrWhiteSpace(reason, nameof(reason), 500);
        UpdatedAt = utcNow;
        return true;
    }

    public bool MarkFailed(string reason, DateTimeOffset utcNow)
    {
        Status = PaymentProviderEventStatus.Failed;
        FailureReason = Guard.NotNullOrWhiteSpace(reason, nameof(reason), 500);
        UpdatedAt = utcNow;
        return true;
    }
}
