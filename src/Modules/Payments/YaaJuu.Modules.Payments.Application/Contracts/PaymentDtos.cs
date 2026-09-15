namespace YaaJuu.Modules.Payments.Application.Contracts;

public sealed record ConsumerPaymentDto(
    Guid PaymentId,
    Guid OrderId,
    string Status,
    decimal Amount,
    string Currency,
    string? CurrentAttemptStatus,
    bool CanRetry,
    bool RequiresAttention,
    PaymentActionDto? Action,
    DateTimeOffset CreatedAt);

public sealed record PaymentActionDto(
    string Kind,
    string? PublicKey,
    string? CheckoutUrl,
    string? MerchantReference,
    long? AmountInCents,
    string? Currency,
    string? IntegritySignature);

public sealed record InitializePaymentRequest(
    string? AcceptanceToken,
    string? AcceptPersonalAuth,
    string? CardToken,
    int? Installments,
    string? CustomerEmail);

public sealed record BusinessPaymentSummaryDto(
    Guid PaymentId,
    Guid OrderId,
    string? OrderNumber,
    decimal Amount,
    string Currency,
    string Status,
    bool RequiresAttention,
    DateTimeOffset CreatedAt);

public sealed record BusinessPaymentDetailDto(
    Guid PaymentId,
    Guid OrderId,
    string? OrderNumber,
    decimal Amount,
    string Currency,
    string Status,
    bool RequiresAttention,
    string? ReconciliationReason,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PaymentAttemptSummaryDto> Attempts);

public sealed record PaymentAttemptSummaryDto(
    Guid AttemptId,
    string Status,
    string MerchantReference,
    string? ProviderTransactionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? DeclinedAt);

public sealed record AdminPaymentSummaryDto(
    Guid PaymentId,
    Guid OrderId,
    Guid TenantId,
    Guid StoreId,
    decimal Amount,
    string Currency,
    string Status,
    bool RequiresReconciliation,
    string ReconciliationReason,
    DateTimeOffset CreatedAt);

public sealed record AdminPaymentDetailDto(
    Guid PaymentId,
    Guid OrderId,
    Guid TenantId,
    Guid StoreId,
    string Provider,
    decimal Amount,
    string Currency,
    string Status,
    bool RequiresReconciliation,
    string ReconciliationReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt,
    IReadOnlyList<PaymentAttemptSummaryDto> Attempts);

public sealed record WompiMerchantWriteRequest(
    string Environment,
    string PublicKey,
    string? PrivateKey,
    string? IntegritySecret,
    string? EventsSecret,
    bool ReplaceSecrets);

public sealed record WompiMerchantConfigurationDto(
    string Provider,
    string Environment,
    bool IsEnabled,
    string PublicKeyMasked,
    bool HasPrivateKey,
    bool HasIntegritySecret,
    bool HasEventsSecret,
    int Version,
    DateTimeOffset UpdatedAt);

public sealed record BusinessPaymentConfigurationStatusDto(
    bool Configured,
    bool Enabled,
    string? Provider,
    string? Environment);
