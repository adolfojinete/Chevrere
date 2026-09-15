using YaaJuu.Modules.Payments.Application;
using YaaJuu.Modules.Payments.Application.Abstractions;
using YaaJuu.Modules.Payments.Application.Services;
using YaaJuu.Modules.Payments.Domain;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Payments;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Time;
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
    IOptions<PaymentsOptions> options)
{
    public async Task RunBatchAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value.Reconciliation;
        var olderThan = clock.UtcNow.AddSeconds(-opts.MinimumAttemptAgeSeconds);
        var attempts = await store.ListAttemptsForReconciliationAsync(opts.BatchSize, olderThan, cancellationToken);

        foreach (var attemptRow in attempts)
        {
            try
            {
                var payment = await store.GetByIdAsync(attemptRow.PaymentId, cancellationToken);
                if (payment is null)
                {
                    continue;
                }

                var attempt = payment.Attempts.FirstOrDefault(a => a.Id == attemptRow.Id);
                if (attempt is null || string.IsNullOrWhiteSpace(attempt.ProviderTransactionId))
                {
                    continue;
                }

                var merchant = await store.GetMerchantByIdAsync(attempt.MerchantConfigurationId, cancellationToken);
                if (merchant is null)
                {
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
                catch (Exception)
                {
                    continue;
                }

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
                    correlation.CorrelationId,
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
            catch (Exception)
            {
                // Per-attempt isolation: continue batch.
            }
        }
    }
}
