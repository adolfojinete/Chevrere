using YaaJuu.Modules.Payments.Application;
using YaaJuu.Modules.Payments.Application.Abstractions;
using YaaJuu.Modules.Payments.Application.Services;
using YaaJuu.Modules.Payments.Domain;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Payments;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace YaaJuu.Modules.Payments.Application.Commands;

public sealed class ReconcilePaymentsHandler(
    IPaymentStore store,
    IPaymentProvider provider,
    IPaymentSecretProtector secrets,
    ProviderStatusProcessor processor,
    ICorrelationContext correlation,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<PaymentsOptions> options,
    ILogger<ReconcilePaymentsHandler> logger)
{
    public async Task RunBatchAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value.Reconciliation;
        var olderThan = clock.UtcNow.AddSeconds(-opts.MinimumAttemptAgeSeconds);
        var attempts = await store.ListAttemptsForReconciliationAsync(opts.BatchSize, olderThan, cancellationToken);
        var correlationId = TryGetCorrelationId();

        foreach (var attemptRow in attempts)
        {
            try
            {
                var payment = await store.GetByIdAsync(attemptRow.PaymentId, cancellationToken);
                if (payment is null)
                {
                    logger.LogError(
                        "Payment reconciliation skipped: payment not found for attempt. PaymentId={PaymentId} AttemptId={AttemptId} CorrelationId={CorrelationId}",
                        attemptRow.PaymentId,
                        attemptRow.Id,
                        correlationId);
                    continue;
                }

                var attempt = payment.Attempts.FirstOrDefault(a => a.Id == attemptRow.Id);
                if (attempt is null)
                {
                    logger.LogError(
                        "Payment reconciliation skipped: attempt missing from aggregate. PaymentId={PaymentId} AttemptId={AttemptId} CorrelationId={CorrelationId}",
                        attemptRow.PaymentId,
                        attemptRow.Id,
                        correlationId);
                    continue;
                }

                // No official lookup-by-reference: without ProviderTransactionId we cannot GET.
                if (string.IsNullOrWhiteSpace(attempt.ProviderTransactionId))
                {
                    continue;
                }

                var merchant = await store.GetMerchantByIdAsync(attempt.MerchantConfigurationId, cancellationToken);
                if (merchant is null)
                {
                    logger.LogError(
                        "Payment reconciliation skipped: historical merchant configuration not found. PaymentId={PaymentId} AttemptId={AttemptId} MerchantConfigurationId={MerchantConfigurationId} CorrelationId={CorrelationId}",
                        payment.Id,
                        attempt.Id,
                        attempt.MerchantConfigurationId,
                        correlationId);
                    continue;
                }

                PaymentProviderMerchantSecrets decrypted;
                try
                {
                    decrypted = new PaymentProviderMerchantSecrets(
                        merchant.PublicKey,
                        secrets.Unprotect(merchant.EncryptedPrivateKey, PaymentSecretPurposes.WompiPrivateKey),
                        secrets.Unprotect(merchant.EncryptedIntegritySecret, PaymentSecretPurposes.WompiIntegritySecret),
                        secrets.Unprotect(merchant.EncryptedEventsSecret, PaymentSecretPurposes.WompiEventsSecret));
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Merchant configuration cannot be decrypted for payment reconciliation. PaymentId={PaymentId} AttemptId={AttemptId} MerchantConfigurationId={MerchantConfigurationId} Provider={Provider} CorrelationId={CorrelationId}",
                        payment.Id,
                        attempt.Id,
                        attempt.MerchantConfigurationId,
                        attempt.Provider,
                        correlationId);
                    continue;
                }

                // Historical attempt Environment/config — never runtime Environment.
                var lookup = await provider.GetTransactionAsync(
                    new PaymentProviderLookupRequest(
                        decrypted,
                        merchant.Environment.ToString(),
                        attempt.ProviderTransactionId),
                    cancellationToken);

                attempt.RecordProviderCheck(clock.UtcNow);
                if (lookup.OutcomeUnknown || !lookup.Succeeded)
                {
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    continue;
                }

                var apply = await processor.ApplyAsync(
                    payment,
                    attempt,
                    lookup.Status,
                    lookup.ProviderTransactionId,
                    lookup.ProviderRawStatus,
                    lookup.Amount,
                    lookup.Currency,
                    lookup.FailureCode,
                    lookup.FailureMessage,
                    lookup.ProviderOccurredAt,
                    correlationId ?? Guid.CreateVersion7().ToString("N"),
                    clock.UtcNow,
                    cancellationToken);

                if (apply.PaymentApprovedMutated)
                {
                    audit.Record(
                        AuditActions.PaymentApproved,
                        nameof(Payment),
                        payment.Id,
                        payment.TenantId,
                        newValue: new { payment.Status, Source = "reconciliation" });
                }

                if (apply.ReconciliationMutated)
                {
                    audit.Record(
                        AuditActions.PaymentReconciliationRequired,
                        nameof(Payment),
                        payment.Id,
                        payment.TenantId,
                        newValue: new { payment.ReconciliationReason, Source = "reconciliation" });
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex,
                    "Unexpected payment reconciliation failure for attempt. PaymentId={PaymentId} AttemptId={AttemptId} Provider={Provider} ProviderTransactionId={ProviderTransactionId} MerchantConfigurationId={MerchantConfigurationId} CorrelationId={CorrelationId}",
                    attemptRow.PaymentId,
                    attemptRow.Id,
                    attemptRow.Provider,
                    attemptRow.ProviderTransactionId,
                    attemptRow.MerchantConfigurationId,
                    correlationId);
            }
        }
    }

    private string? TryGetCorrelationId()
    {
        try
        {
            return correlation.CorrelationId;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
