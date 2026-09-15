using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YaaJuu.Modules.Payments.Application.Abstractions;
using YaaJuu.Modules.Payments.Application.Services;
using YaaJuu.Modules.Payments.Domain;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using YaaJuu.SharedKernel.Time;

namespace YaaJuu.Modules.Payments.Application.Commands;

public sealed record ProcessWompiWebhookCommand(
    string RawBody,
    string? ChecksumHeader);

public sealed class ProcessWompiWebhookHandler(
    IPaymentStore store,
    IPaymentSecretProtector secrets,
    ProviderStatusProcessor processor,
    ICorrelationContext correlation,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<ProcessWompiWebhookCommand, Result<WebhookProcessOutcome>>
{
    public async Task<Result<WebhookProcessOutcome>> HandleAsync(
        ProcessWompiWebhookCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RawBody) || request.RawBody.Length > 64_000)
        {
            return Result.Failure<WebhookProcessOutcome>(
                Error.Validation("payment.webhook.invalid", "Invalid webhook payload."));
        }

        using var document = JsonDocument.Parse(request.RawBody);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("transaction", out var tx))
        {
            return Result.Failure<WebhookProcessOutcome>(
                Error.Validation("payment.webhook.invalid", "Invalid webhook payload."));
        }

        var reference = tx.TryGetProperty("reference", out var refEl) ? refEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return Result.Failure<WebhookProcessOutcome>(
                Error.Validation("payment.webhook.invalid", "Invalid webhook payload."));
        }

        var attemptRow = await store.GetAttemptByMerchantReferenceAsync(reference, cancellationToken);
        if (attemptRow is null)
        {
            return Result.Success(WebhookProcessOutcome.IgnoredUnknown);
        }

        var payment = await store.GetByIdAsync(attemptRow.PaymentId, cancellationToken);
        if (payment is null)
        {
            return Result.Success(WebhookProcessOutcome.IgnoredUnknown);
        }

        var attempt = payment.Attempts.First(a => a.Id == attemptRow.Id);
        var merchant = await store.GetMerchantByIdAsync(attempt.MerchantConfigurationId, cancellationToken);
        if (merchant is null)
        {
            return Result.Failure<WebhookProcessOutcome>(
                Error.Failure("payment.webhook.invalid_signature", "Invalid webhook."));
        }

        string eventsSecret;
        try
        {
            eventsSecret = secrets.Unprotect(merchant.EncryptedEventsSecret, PaymentSecretPurposes.WompiEventsSecret);
        }
        catch (Exception)
        {
            return Result.Failure<WebhookProcessOutcome>(
                Error.Failure("payment.webhook.invalid_signature", "Invalid webhook."));
        }

        if (!TryVerifyChecksum(root, eventsSecret, request.ChecksumHeader))
        {
            return Result.Failure<WebhookProcessOutcome>(
                Error.Unauthorized("payment.webhook.invalid_signature", "Invalid webhook."));
        }

        var eventId = BuildEventId(root, reference);
        if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
        {
            eventId = idEl.GetString() ?? eventId;
        }
        else if (root.TryGetProperty("signature", out var sig)
                 && sig.TryGetProperty("checksum", out var cs)
                 && root.TryGetProperty("timestamp", out var ts))
        {
            eventId = $"{cs.GetString()}:{ts.GetRawText()}";
        }

        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.RawBody)))
            .ToLowerInvariant();

        var existing = await store.GetProviderEventAsync(
            PaymentProvider.Wompi, attempt.Environment, eventId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
            {
                existing.MarkAnomaly("payload_hash_mismatch", clock.UtcNow);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Result.Failure<WebhookProcessOutcome>(
                    Error.Conflict("payment.webhook.anomaly", "Webhook anomaly detected."));
            }

            return Result.Success(WebhookProcessOutcome.Duplicate);
        }

        var eventType = root.TryGetProperty("event", out var ev) ? ev.GetString() ?? "transaction.updated" : "transaction.updated";
        var providerEvent = PaymentProviderEvent.CreateTrusted(
            PaymentProvider.Wompi,
            attempt.Environment,
            eventId,
            eventType,
            payloadHash,
            attempt.Id,
            merchant.Id,
            clock.UtcNow,
            null);
        store.Add(providerEvent);

        var statusRaw = tx.TryGetProperty("status", out var st) ? st.GetString() : null;
        var providerTx = tx.TryGetProperty("id", out var txId) ? txId.GetString() : null;
        long? cents = tx.TryGetProperty("amount_in_cents", out var am) && am.TryGetInt64(out var c) ? c : null;
        var currency = tx.TryGetProperty("currency", out var cur) ? cur.GetString() : null;

        try
        {
            var apply = await processor.ApplyAsync(
                payment,
                attempt,
                WompiContract.MapStatus(statusRaw),
                providerTx,
                statusRaw,
                cents is null ? null : WompiAmountConverter.FromAmountInCents(cents.Value),
                currency,
                null,
                null,
                null,
                correlation.CorrelationId,
                clock.UtcNow,
                cancellationToken);

            providerEvent.MarkProcessed(clock.UtcNow);

            if (apply.PaymentApprovedMutated)
            {
                audit.Record(
                    AuditActions.PaymentApproved,
                    nameof(Payment),
                    payment.Id,
                    payment.TenantId,
                    newValue: new { payment.Status, payment.Amount });
            }

            if (apply.ReconciliationMutated)
            {
                audit.Record(
                    AuditActions.PaymentReconciliationRequired,
                    nameof(Payment),
                    payment.Id,
                    payment.TenantId,
                    newValue: new { payment.ReconciliationReason });
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(WebhookProcessOutcome.Processed);
        }
        catch (DuplicateKeyException)
        {
            store.DiscardPendingProviderEvent(providerEvent);
            audit.DiscardPending(AuditActions.PaymentApproved, nameof(Payment), payment.Id);
            audit.DiscardPending(AuditActions.PaymentReconciliationRequired, nameof(Payment), payment.Id);
            return Result.Success(WebhookProcessOutcome.Duplicate);
        }
    }

    private static string BuildEventId(JsonElement root, string reference)
    {
        if (root.TryGetProperty("timestamp", out var ts))
        {
            return $"{reference}:{ts.GetRawText()}";
        }

        return reference;
    }

    private static bool TryVerifyChecksum(JsonElement root, string eventsSecret, string? headerChecksum)
    {
        if (!root.TryGetProperty("signature", out var signature)
            || !signature.TryGetProperty("properties", out var properties)
            || !signature.TryGetProperty("checksum", out var checksumEl)
            || !root.TryGetProperty("timestamp", out var timestampEl)
            || !root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("transaction", out var tx))
        {
            return false;
        }

        var checksum = headerChecksum ?? checksumEl.GetString();
        if (string.IsNullOrWhiteSpace(checksum) || !timestampEl.TryGetInt64(out var timestamp))
        {
            return false;
        }

        var values = new List<string>();
        foreach (var prop in properties.EnumerateArray())
        {
            var path = prop.GetString();
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("transaction.", StringComparison.Ordinal))
            {
                return false;
            }

            var field = path["transaction.".Length..];
            if (!tx.TryGetProperty(field, out var valueEl))
            {
                return false;
            }

            values.Add(valueEl.ValueKind switch
            {
                JsonValueKind.String => valueEl.GetString() ?? string.Empty,
                JsonValueKind.Number => valueEl.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => valueEl.GetRawText()
            });
        }

        return WompiContract.VerifyEventChecksum(eventsSecret, values, timestamp, checksum);
    }
}

public enum WebhookProcessOutcome
{
    Processed = 1,
    Duplicate = 2,
    IgnoredUnknown = 3
}
