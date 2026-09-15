namespace YaaJuu.SharedKernel.Payments;

/// <summary>
/// Minimal provider port. Wompi adapter lives in Payments.Infrastructure.
/// </summary>
public interface IPaymentProvider
{
    PaymentProviderKind Kind { get; }

    Task<PaymentProviderInitializeResult> InitializeWidgetAsync(
        PaymentProviderInitializeRequest request,
        CancellationToken cancellationToken);

    Task<PaymentProviderTransactionResult> CreateCardTransactionAsync(
        PaymentProviderCardChargeRequest request,
        CancellationToken cancellationToken);

    Task<PaymentProviderTransactionResult> GetTransactionAsync(
        PaymentProviderLookupRequest request,
        CancellationToken cancellationToken);
}

public enum PaymentProviderKind
{
    Wompi = 1
}

public enum PaymentProviderOutcomeStatus
{
    Pending = 1,
    Approved = 2,
    Declined = 3,
    Voided = 4,
    Error = 5,
    Unknown = 6
}

public sealed record PaymentProviderMerchantSecrets(
    string PublicKey,
    string PrivateKey,
    string IntegritySecret,
    string EventsSecret);

public sealed record PaymentProviderInitializeRequest(
    PaymentProviderMerchantSecrets Secrets,
    string Environment,
    string MerchantReference,
    decimal Amount,
    string Currency,
    string? CustomerEmail,
    string? RedirectUrl);

public sealed record PaymentProviderInitializeResult(
    bool Succeeded,
    PaymentProviderOutcomeStatus Status,
    string? ProviderTransactionId,
    string? ProviderRawStatus,
    string? FailureCode,
    string? FailureMessage,
    PaymentClientAction? ClientAction,
    bool OutcomeUnknown);

public sealed record PaymentProviderCardChargeRequest(
    PaymentProviderMerchantSecrets Secrets,
    string Environment,
    string MerchantReference,
    decimal Amount,
    string Currency,
    string AcceptanceToken,
    string AcceptPersonalAuth,
    string CustomerEmail,
    string CardToken,
    int Installments,
    string? RedirectUrl);

public sealed record PaymentProviderLookupRequest(
    PaymentProviderMerchantSecrets Secrets,
    string Environment,
    string ProviderTransactionId);

public sealed record PaymentProviderTransactionResult(
    bool Succeeded,
    PaymentProviderOutcomeStatus Status,
    string? ProviderTransactionId,
    string? ProviderRawStatus,
    decimal? Amount,
    string? Currency,
    string? MerchantReference,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset? ProviderOccurredAt,
    bool OutcomeUnknown);

public sealed record PaymentClientAction(
    string Kind,
    string? PublicKey,
    string? CheckoutUrl,
    string? MerchantReference,
    long? AmountInCents,
    string? Currency,
    string? IntegritySignature);
