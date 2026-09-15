namespace YaaJuu.SharedKernel.Payments;

/// <summary>
/// Minimal Order lifecycle port used by Payments. Orders owns Confirm semantics;
/// Payments must not mutate Order aggregates directly.
/// </summary>
public interface IOrderPaymentLifecycle
{
    Task<PayableOrderSnapshot?> GetPayableOrderAsync(
        Guid orderId,
        Guid consumerUserId,
        CancellationToken cancellationToken);

    Task<PayableOrderSnapshot?> GetOrderForPaymentAsync(
        Guid orderId,
        CancellationToken cancellationToken);

    Task<ConfirmPaidOrderResult> ConfirmPaidOrderAsync(
        Guid orderId,
        Guid tenantId,
        Guid storeId,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed record PayableOrderSnapshot(
    Guid OrderId,
    Guid ConsumerUserId,
    Guid TenantId,
    Guid StoreId,
    string Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset ExpiresAt,
    bool TenantIsActive,
    bool StoreIsActive);

public enum ConfirmPaidOrderOutcome
{
    Confirmed = 1,
    AlreadyConfirmed = 2,
    Cancelled = 3,
    Expired = 4,
    NotFound = 5,
    ConcurrencyConflict = 6,
    NotPendingPayment = 7
}

public sealed record ConfirmPaidOrderResult(ConfirmPaidOrderOutcome Outcome);
