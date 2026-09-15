using System.Globalization;
using YaaJuu.Modules.Payments.Domain;
using YaaJuu.SharedKernel.Domain.ValueObjects;
using YaaJuu.SharedKernel.Payments;

namespace YaaJuu.Modules.Payments.Application.Services;

public static class WompiAmountConverter
{
    public static long ToAmountInCents(Money money)
    {
        ArgumentNullException.ThrowIfNull(money);
        if (!string.Equals(money.Currency, CurrencyCodes.Cop, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only COP is supported for Wompi conversion.");
        }

        var scaled = decimal.Multiply(money.Amount, 100m);
        if (scaled != decimal.Truncate(scaled))
        {
            throw new InvalidOperationException("Amount is not representable in Wompi centavos.");
        }

        return decimal.ToInt64(scaled);
    }

    public static decimal FromAmountInCents(long amountInCents) =>
        decimal.Divide(amountInCents, 100m);

    public static string FormatCents(long amountInCents) =>
        amountInCents.ToString(CultureInfo.InvariantCulture);
}

public sealed class ApplyProviderStatusResult(
    bool AttemptMutated,
    bool PaymentApprovedMutated,
    bool ReconciliationMutated,
    ConfirmPaidOrderOutcome? OrderOutcome)
{
    public bool AttemptMutated { get; } = AttemptMutated;
    public bool PaymentApprovedMutated { get; } = PaymentApprovedMutated;
    public bool ReconciliationMutated { get; } = ReconciliationMutated;
    public ConfirmPaidOrderOutcome? OrderOutcome { get; } = OrderOutcome;
}

/// <summary>
/// Shared processor for webhook, reconciliation and immediate provider responses.
/// </summary>
public sealed class ProviderStatusProcessor(IOrderPaymentLifecycle orders)
{
    public async Task<ApplyProviderStatusResult> ApplyAsync(
        Payment payment,
        PaymentAttempt attempt,
        PaymentProviderOutcomeStatus status,
        string? providerTransactionId,
        string? providerRawStatus,
        decimal? providerAmount,
        string? providerCurrency,
        string? failureCode,
        string? failureMessage,
        DateTimeOffset? providerOccurredAt,
        string correlationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(attempt);

        // Status first: never fabricate Approved from amount/currency mismatch alone.
        switch (status)
        {
            case PaymentProviderOutcomeStatus.Pending:
                {
                    var mutated = attempt.MarkPending(providerTransactionId, providerRawStatus, null, utcNow);
                    var recon = MarkNonApprovedFinancialMismatch(payment, providerAmount, providerCurrency, utcNow);
                    return new ApplyProviderStatusResult(mutated, false, recon, null);
                }
            case PaymentProviderOutcomeStatus.Declined:
                {
                    var mutated = attempt.MarkDeclined(
                        providerTransactionId, providerRawStatus, failureCode, failureMessage, utcNow, providerOccurredAt);
                    var recon = MarkNonApprovedFinancialMismatch(payment, providerAmount, providerCurrency, utcNow);
                    return new ApplyProviderStatusResult(mutated, false, recon, null);
                }
            case PaymentProviderOutcomeStatus.Voided:
                {
                    var mutated = attempt.MarkVoided(providerTransactionId, providerRawStatus, utcNow, providerOccurredAt);
                    var recon = MarkNonApprovedFinancialMismatch(payment, providerAmount, providerCurrency, utcNow);
                    return new ApplyProviderStatusResult(mutated, false, recon, null);
                }
            case PaymentProviderOutcomeStatus.Error:
                {
                    var mutated = attempt.MarkError(failureCode, failureMessage, utcNow);
                    var recon = MarkNonApprovedFinancialMismatch(payment, providerAmount, providerCurrency, utcNow);
                    return new ApplyProviderStatusResult(mutated, false, recon, null);
                }
            case PaymentProviderOutcomeStatus.Unknown:
                {
                    var mutated = attempt.MarkUnknown(providerTransactionId, providerRawStatus, utcNow);
                    var recon = MarkNonApprovedFinancialMismatch(payment, providerAmount, providerCurrency, utcNow);
                    return new ApplyProviderStatusResult(mutated, false, recon, null);
                }
            case PaymentProviderOutcomeStatus.Approved:
                return await ApplyApprovedAsync(
                    payment,
                    attempt,
                    providerTransactionId,
                    providerRawStatus,
                    providerAmount,
                    providerCurrency,
                    providerOccurredAt,
                    correlationId,
                    utcNow,
                    cancellationToken);
            default:
                {
                    var mutated = attempt.MarkUnknown(providerTransactionId, providerRawStatus ?? status.ToString(), utcNow);
                    var recon = payment.MarkRequiresReconciliation(ReconciliationReason.UnexpectedProviderState, utcNow);
                    return new ApplyProviderStatusResult(mutated, false, recon, null);
                }
        }
    }

    private static bool MarkNonApprovedFinancialMismatch(
        Payment payment,
        decimal? providerAmount,
        string? providerCurrency,
        DateTimeOffset utcNow)
    {
        if (providerAmount is not null && providerAmount.Value != payment.Amount)
        {
            return payment.MarkRequiresReconciliation(ReconciliationReason.AmountMismatch, utcNow);
        }

        if (providerCurrency is not null
            && !string.Equals(providerCurrency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return payment.MarkRequiresReconciliation(ReconciliationReason.CurrencyMismatch, utcNow);
        }

        return false;
    }

    private async Task<ApplyProviderStatusResult> ApplyApprovedAsync(
        Payment payment,
        PaymentAttempt attempt,
        string? providerTransactionId,
        string? providerRawStatus,
        decimal? providerAmount,
        string? providerCurrency,
        DateTimeOffset? providerOccurredAt,
        string correlationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerTransactionId) && string.IsNullOrWhiteSpace(attempt.ProviderTransactionId))
        {
            var unknown = attempt.MarkUnknown(null, providerRawStatus, utcNow);
            var recon = payment.MarkRequiresReconciliation(ReconciliationReason.UnexpectedProviderState, utcNow);
            return new ApplyProviderStatusResult(unknown, false, recon, null);
        }

        var txId = providerTransactionId ?? attempt.ProviderTransactionId!;
        var attemptMutated = attempt.MarkApproved(txId, providerRawStatus, utcNow, providerOccurredAt);
        var paymentMutated = payment.MarkApproved(attempt, utcNow);

        // Authentic Approved preserves external truth; Order confirm requires exact financial match.
        if (providerAmount is null || string.IsNullOrWhiteSpace(providerCurrency))
        {
            var recon = payment.MarkRequiresReconciliation(ReconciliationReason.UnexpectedProviderState, utcNow);
            return new ApplyProviderStatusResult(attemptMutated, paymentMutated, recon, null);
        }

        if (providerAmount.Value != payment.Amount)
        {
            var recon = payment.MarkRequiresReconciliation(ReconciliationReason.AmountMismatch, utcNow);
            return new ApplyProviderStatusResult(attemptMutated, paymentMutated, recon, null);
        }

        if (!string.Equals(providerCurrency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            var recon = payment.MarkRequiresReconciliation(ReconciliationReason.CurrencyMismatch, utcNow);
            return new ApplyProviderStatusResult(attemptMutated, paymentMutated, recon, null);
        }

        var confirm = await orders.ConfirmPaidOrderAsync(
            payment.OrderId,
            payment.TenantId,
            payment.StoreId,
            correlationId,
            cancellationToken);

        var reconMutated = false;
        if (confirm.Outcome is ConfirmPaidOrderOutcome.Expired)
        {
            reconMutated = payment.MarkRequiresReconciliation(
                ReconciliationReason.OrderExpiredAfterPayment, utcNow);
        }
        else if (confirm.Outcome is ConfirmPaidOrderOutcome.Cancelled)
        {
            reconMutated = payment.MarkRequiresReconciliation(
                ReconciliationReason.OrderCancelledAfterPayment, utcNow);
        }
        else if (confirm.Outcome is ConfirmPaidOrderOutcome.NotFound
                 or ConfirmPaidOrderOutcome.NotPendingPayment
                 or ConfirmPaidOrderOutcome.ConcurrencyConflict)
        {
            reconMutated = payment.MarkRequiresReconciliation(
                ReconciliationReason.OrderConfirmFailed, utcNow);
        }

        return new ApplyProviderStatusResult(attemptMutated, paymentMutated, reconMutated, confirm.Outcome);
    }
}
