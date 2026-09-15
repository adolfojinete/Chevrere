using Microsoft.Extensions.Options;
using System.Text.Json;
using YaaJuu.Modules.Payments.Application.Abstractions;
using YaaJuu.Modules.Payments.Application.Contracts;
using YaaJuu.Modules.Payments.Domain;
using YaaJuu.Modules.Payments.Application.Services;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Domain;
using YaaJuu.SharedKernel.Domain.ValueObjects;
using YaaJuu.SharedKernel.Idempotency;
using YaaJuu.SharedKernel.Payments;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using YaaJuu.SharedKernel.Time;

namespace YaaJuu.Modules.Payments.Application.Commands;

public sealed record InitializePaymentCommand(
    Guid OrderId,
    string IdempotencyKey,
    InitializePaymentRequest Body);

public sealed class InitializePaymentHandler(
    IPaymentStore store,
    IOrderPaymentLifecycle orders,
    IPaymentProvider provider,
    IPaymentSecretProtector secrets,
    IIdempotencyStore idempotency,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<PaymentsOptions> optionsAccessor)
    : IHandler<InitializePaymentCommand, Result<ConsumerPaymentDto>>
{
    private const string UnavailableMessage = "Payment currently unavailable.";

    public async Task<Result<ConsumerPaymentDto>> HandleAsync(
        InitializePaymentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!optionsAccessor.Value.Enabled)
        {
            return Result.Failure<ConsumerPaymentDto>(
                Error.Conflict("payment.unavailable", "Payments are currently unavailable."));
        }

        if (!IdempotencyKeyRules.IsValid(request.IdempotencyKey))
        {
            return Result.Failure<ConsumerPaymentDto>(Error.Validation(
                "payment.idempotency_key.required",
                "Idempotency-Key is required (8-128 chars, no whitespace)."));
        }

        if (currentUser.UserId is null)
        {
            return Result.Failure<ConsumerPaymentDto>(Error.Unauthorized("auth.required", "Authentication required."));
        }

        var consumerId = currentUser.UserId.Value;
        var payable = await orders.GetPayableOrderAsync(request.OrderId, consumerId, cancellationToken);
        if (payable is null)
        {
            return Result.Failure<ConsumerPaymentDto>(Error.NotFound("payment.not_found", "Payment not found."));
        }

        var existingKey = await idempotency.FindAsync(
            payable.TenantId, IdempotencyOperations.PaymentsInitialize, request.IdempotencyKey, cancellationToken);

        var payment = await store.GetByOrderIdAsync(payable.OrderId, cancellationToken);
        // Fingerprint must be stable across create + replay. Do not include Payment.Id:
        // on first call payment is null; after commit it exists and would break same-key replay.
        var fingerprint = BuildFingerprint(consumerId, payable, request.Body);

        if (existingKey is not null)
        {
            if (!string.Equals(existingKey.RequestHash, fingerprint, StringComparison.Ordinal))
            {
                return Result.Failure<ConsumerPaymentDto>(
                    Error.Conflict("idempotency.key.reused", "Idempotency-Key was already used with a different payload."));
            }

            payment ??= existingKey.ResourceId is null
                ? null
                : await store.GetByIdAsync(existingKey.ResourceId.Value, cancellationToken);
            if (payment is null)
            {
                return Result.Failure<ConsumerPaymentDto>(Error.NotFound("payment.not_found", "Payment not found."));
            }

            return Result.Success(await MapConsumerAsync(payment, store, secrets, cancellationToken));
        }

        var gate = ValidatePayableWithClock(payable, payment);
        if (gate is not null)
        {
            return Result.Failure<ConsumerPaymentDto>(gate);
        }

        var merchant = await store.GetActiveMerchantAsync(payable.TenantId, PaymentProvider.Wompi, cancellationToken);
        if (merchant is null)
        {
            return Result.Failure<ConsumerPaymentDto>(
                Error.Conflict("payment.merchant.not_configured", UnavailableMessage));
        }

        if (!merchant.IsEnabled)
        {
            return Result.Failure<ConsumerPaymentDto>(
                Error.Conflict("payment.merchant.disabled", UnavailableMessage));
        }

        payment ??= Payment.CreateForOrder(
            payable.OrderId,
            payable.TenantId,
            payable.StoreId,
            Money.FromPersistence(payable.TotalAmount, payable.Currency),
            PaymentProvider.Wompi,
            clock.UtcNow);

        var isNewPayment = payment.Attempts.Count == 0 && await store.GetByOrderIdAsync(payable.OrderId, cancellationToken) is null;
        if (isNewPayment)
        {
            store.Add(payment);
            audit.Record(
                AuditActions.PaymentCreated,
                nameof(Payment),
                payment.Id,
                payment.TenantId,
                newValue: new { payment.OrderId, payment.Amount, payment.Currency, payment.Provider });
        }

        if (payment.GetInFlightAttempt() is not null)
        {
            return Result.Failure<ConsumerPaymentDto>(
                Error.Conflict("payment.attempt.in_progress", "A payment attempt is already in progress."));
        }

        if (payment.Status == PaymentStatus.Approved || payment.RequiresReconciliation)
        {
            return Result.Success(await MapConsumerAsync(payment, store, secrets, cancellationToken));
        }

        // Reload when retrying after a terminal attempt so xmin matches the row written by
        // webhook/reconciliation (avoids false DbUpdateConcurrencyException on StartAttempt).
        if (payment.Attempts.Count > 0)
        {
            store.DetachPaymentGraph(payment);
            payment = await store.GetByOrderIdAsync(payable.OrderId, cancellationToken)
                      ?? payment;
            if (payment.GetInFlightAttempt() is not null)
            {
                return Result.Failure<ConsumerPaymentDto>(
                    Error.Conflict("payment.attempt.in_progress", "A payment attempt is already in progress."));
            }

            if (payment.Status == PaymentStatus.Approved || payment.RequiresReconciliation)
            {
                return Result.Success(await MapConsumerAsync(payment, store, secrets, cancellationToken));
            }
        }

        PaymentAttempt attempt;
        try
        {
            attempt = payment.StartAttempt(
                merchant.Id,
                merchant.Environment,
                Guid.CreateVersion7().ToString("N"),
                clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure<ConsumerPaymentDto>(Error.Conflict(ex.Code, ex.Message));
        }

        audit.Record(
            AuditActions.PaymentAttemptCreated,
            nameof(PaymentAttempt),
            attempt.Id,
            payment.TenantId,
            newValue: new { attempt.PaymentId, attempt.MerchantReference, attempt.Amount, attempt.Currency });

        var idempo = new IdempotentOperation
        {
            Id = Guid.CreateVersion7(),
            TenantId = payable.TenantId,
            Operation = IdempotencyOperations.PaymentsInitialize,
            IdempotencyKey = request.IdempotencyKey,
            RequestHash = fingerprint,
            ResourceId = payment.Id,
            CreatedAt = clock.UtcNow
        };
        idempotency.Add(idempo);

        if (!isNewPayment)
        {
            store.AcceptHistoricalAttemptsUnchanged(payment, attempt);
        }

        try
        {
            using (isNewPayment ? null : store.SuspendAutoDetectChanges())
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DuplicateKeyException)
        {
            return await RecoverInitializeConflictAsync(
                payable, request, fingerprint, payment, attempt, isNewPayment, idempo, cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return await RecoverInitializeConflictAsync(
                payable, request, fingerprint, payment, attempt, isNewPayment, idempo, cancellationToken);
        }

        // Reload tracked payment after commit
        payment = await store.GetByOrderIdAsync(payable.OrderId, cancellationToken)
                  ?? payment;
        attempt = payment.Attempts.First(a => a.Id == attempt.Id);

        if (!attempt.TryClaimInitiation(clock.UtcNow))
        {
            return Result.Success(await MapConsumerAsync(payment, store, secrets, cancellationToken));
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            var latest = await store.GetByOrderIdAsync(payable.OrderId, cancellationToken)
                         ?? payment;
            return Result.Success(await MapConsumerAsync(latest, store, secrets, cancellationToken));
        }

        PaymentProviderMerchantSecrets decrypted;
        try
        {
            decrypted = Decrypt(merchant, secrets);
        }
        catch (Exception)
        {
            attempt.MarkUnknown(null, "secret_decrypt_failed", clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<ConsumerPaymentDto>(
                Error.Failure("payment.merchant.invalid", UnavailableMessage));
        }

        var envName = merchant.Environment.ToString();
        var body = request.Body;
        if (!string.IsNullOrWhiteSpace(body.CardToken)
            && !string.IsNullOrWhiteSpace(body.AcceptanceToken)
            && !string.IsNullOrWhiteSpace(body.AcceptPersonalAuth)
            && !string.IsNullOrWhiteSpace(body.CustomerEmail))
        {
            var charge = await provider.CreateCardTransactionAsync(
                new PaymentProviderCardChargeRequest(
                    decrypted,
                    envName,
                    attempt.MerchantReference,
                    payment.Amount,
                    payment.Currency,
                    body.AcceptanceToken!,
                    body.AcceptPersonalAuth!,
                    body.CustomerEmail!,
                    body.CardToken!,
                    body.Installments is > 0 ? body.Installments.Value : 1,
                    optionsAccessor.Value.Wompi.DefaultRedirectUrl),
                CancellationToken.None);

            await ApplyChargeResultAsync(payment, attempt, charge, cancellationToken);
        }
        else
        {
            var init = await provider.InitializeWidgetAsync(
                new PaymentProviderInitializeRequest(
                    decrypted,
                    envName,
                    attempt.MerchantReference,
                    payment.Amount,
                    payment.Currency,
                    body.CustomerEmail,
                    optionsAccessor.Value.Wompi.DefaultRedirectUrl),
                CancellationToken.None);

            if (init.OutcomeUnknown)
            {
                attempt.MarkUnknown(init.ProviderTransactionId, init.ProviderRawStatus, clock.UtcNow);
            }
            else if (init.Succeeded)
            {
                var actionJson = init.ClientAction is null
                    ? null
                    : JsonSerializer.Serialize(init.ClientAction);
                attempt.MarkPending(init.ProviderTransactionId, init.ProviderRawStatus ?? "PENDING", actionJson, clock.UtcNow);
            }
            else
            {
                attempt.MarkError(init.FailureCode, init.FailureMessage, clock.UtcNow);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await MapConsumerAsync(payment, store, secrets, cancellationToken));
    }

    private async Task<Result<ConsumerPaymentDto>> RecoverInitializeConflictAsync(
        PayableOrderSnapshot payable,
        InitializePaymentCommand request,
        string fingerprint,
        Payment payment,
        PaymentAttempt attempt,
        bool isNewPayment,
        IdempotentOperation idempo,
        CancellationToken cancellationToken)
    {
        idempotency.DiscardPending(idempo);
        audit.DiscardPending(AuditActions.PaymentAttemptCreated, nameof(PaymentAttempt), attempt.Id);
        if (isNewPayment)
        {
            audit.DiscardPending(AuditActions.PaymentCreated, nameof(Payment), payment.Id);
            store.DiscardPendingPayment(payment);
        }
        else
        {
            store.DiscardPendingAttempt(attempt);
        }

        store.DetachPaymentGraph(payment);

        var committed = await store.GetByOrderIdAsync(payable.OrderId, cancellationToken);
        if (committed is null)
        {
            return Result.Failure<ConsumerPaymentDto>(Error.Conflict("payment.conflict", "Payment conflict."));
        }

        var replayKey = await idempotency.FindAsync(
            payable.TenantId, IdempotencyOperations.PaymentsInitialize, request.IdempotencyKey, cancellationToken);
        if (replayKey is not null
            && string.Equals(replayKey.RequestHash, fingerprint, StringComparison.Ordinal))
        {
            return Result.Success(await MapConsumerAsync(committed, store, secrets, cancellationToken));
        }

        if (committed.GetInFlightAttempt() is not null)
        {
            return Result.Failure<ConsumerPaymentDto>(
                Error.Conflict("payment.attempt.in_progress", "A payment attempt is already in progress."));
        }

        return Result.Failure<ConsumerPaymentDto>(Error.Conflict("payment.conflict", "Payment conflict."));
    }

    private async Task ApplyChargeResultAsync(
        Payment payment,
        PaymentAttempt attempt,
        PaymentProviderTransactionResult charge,
        CancellationToken cancellationToken)
    {
        var processor = new ProviderStatusProcessor(orders);
        if (charge.OutcomeUnknown)
        {
            attempt.MarkUnknown(charge.ProviderTransactionId, charge.ProviderRawStatus, clock.UtcNow);
            return;
        }

        var result = await processor.ApplyAsync(
            payment,
            attempt,
            charge.Status,
            charge.ProviderTransactionId,
            charge.ProviderRawStatus,
            charge.Amount,
            charge.Currency,
            charge.FailureCode,
            charge.FailureMessage,
            charge.ProviderOccurredAt,
            correlation.CorrelationId,
            clock.UtcNow,
            cancellationToken);

        if (result.PaymentApprovedMutated)
        {
            audit.Record(
                AuditActions.PaymentApproved,
                nameof(Payment),
                payment.Id,
                payment.TenantId,
                newValue: new { payment.Status, payment.Amount, payment.Currency });
        }

        if (result.ReconciliationMutated)
        {
            audit.Record(
                AuditActions.PaymentReconciliationRequired,
                nameof(Payment),
                payment.Id,
                payment.TenantId,
                newValue: new { payment.ReconciliationReason });
        }
    }

    private static Error? ValidatePayable(PayableOrderSnapshot payable, Payment? payment)
    {
        if (!payable.TenantIsActive || !payable.StoreIsActive)
        {
            return Error.Conflict("payment.unavailable", UnavailableMessage);
        }

        if (string.Equals(payable.Status, "Expired", StringComparison.OrdinalIgnoreCase))
        {
            return Error.Conflict("payment.order.expired", "The order has expired.");
        }

        if (string.Equals(payable.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return Error.Conflict("payment.order.cancelled", "The order was cancelled.");
        }

        if (string.Equals(payable.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
        {
            return payment is null
                ? Error.Conflict("payment.already_approved", "The order is already confirmed.")
                : null;
        }

        if (!string.Equals(payable.Status, "PendingPayment", StringComparison.OrdinalIgnoreCase))
        {
            return Error.Conflict("payment.order.not_payable", "The order is not payable.");
        }

        return null;
    }

    private Error? ValidatePayableWithClock(PayableOrderSnapshot payable, Payment? payment)
    {
        var baseError = ValidatePayable(payable, payment);
        if (baseError is not null)
        {
            return baseError;
        }

        if (string.Equals(payable.Status, "PendingPayment", StringComparison.OrdinalIgnoreCase)
            && clock.UtcNow >= payable.ExpiresAt)
        {
            return Error.Conflict("payment.order.expired", "The order has expired.");
        }

        return null;
    }

    private static string BuildFingerprint(
        Guid consumerId,
        PayableOrderSnapshot payable,
        InitializePaymentRequest body) =>
        IdempotencyFingerprint.Sha256(
            consumerId.ToString("D"),
            payable.OrderId.ToString("D"),
            IdempotencyFingerprint.Format(payable.TotalAmount),
            payable.Currency,
            body.CardToken is null ? "widget" : "card",
            string.IsNullOrWhiteSpace(body.CardToken)
                ? null
                : IdempotencyFingerprint.Sha256(body.CardToken));

    private static PaymentProviderMerchantSecrets Decrypt(
        PaymentMerchantConfiguration merchant,
        IPaymentSecretProtector secrets) =>
        new(
            merchant.PublicKey,
            secrets.Unprotect(merchant.EncryptedPrivateKey, PaymentSecretPurposes.WompiPrivateKey),
            secrets.Unprotect(merchant.EncryptedIntegritySecret, PaymentSecretPurposes.WompiIntegritySecret),
            secrets.Unprotect(merchant.EncryptedEventsSecret, PaymentSecretPurposes.WompiEventsSecret));

    internal static async Task<ConsumerPaymentDto> MapConsumerAsync(
        Payment payment,
        IPaymentStore store,
        IPaymentSecretProtector secrets,
        CancellationToken cancellationToken)
    {
        _ = store;
        _ = secrets;
        _ = cancellationToken;
        var attempt = payment.GetLatestAttempt();
        PaymentActionDto? action = null;
        if (attempt?.CheckoutActionJson is not null)
        {
            action = JsonSerializer.Deserialize<PaymentActionDto>(attempt.CheckoutActionJson);
        }

        var canRetry = payment.CanStartNewAttempt()
                       && payment.Status != PaymentStatus.Approved
                       && !payment.RequiresReconciliation;

        return new ConsumerPaymentDto(
            payment.Id,
            payment.OrderId,
            payment.Status.ToString(),
            payment.Amount,
            payment.Currency,
            attempt?.Status.ToString(),
            canRetry,
            payment.RequiresReconciliation,
            action,
            payment.CreatedAt);
    }
}
