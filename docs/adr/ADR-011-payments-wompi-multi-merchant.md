# ADR-011 — Payments + Wompi multi-comercio

- Status: Accepted
- Date: 2026-09-15
- Wompi docs consulted: 2026-09-14 / 2026-09-15; revalidated 2026-09-15 (Hardening 8.2)

## Context

YaaJuu must collect consumer Order payments without becoming the central merchant of record. Each associate (Tenant) operates their own Wompi commerce credentials. Orders already create `PendingPayment` with an Active inventory reservation; payment success must Confirm the Order without committing inventory.

## Decision

Introduce bounded context `Payments` (Domain / Application / Infrastructure) with:

- `Payment` (1 per Order) + `PaymentAttempt` (N)
- Tenant-scoped versioned `PaymentMerchantConfiguration` (Wompi)
- AES-256-GCM secret protection. Master key (`YAAJUU_PAYMENT_SECRETS_KEY` / `Payments:SecretsMasterKey`) must be **standard Base64 decoding to exactly 32 bytes**. Weak strings are never padded/truncated into a key. Development/Testing may use a deterministic fallback only when no key is configured.
- `IPaymentProvider` abstraction; Wompi adapter only in Infrastructure
- `IOrderPaymentLifecycle` in SharedKernel for Confirm (Orders owns `Order.Confirm`)
- Webhook `POST /api/v1/webhooks/wompi` with official checksum verification
- Reconciliation worker for Pending/Unknown (lookup by provider transaction id)
- No central collection, split, payout, wallet, refund automation, or saved cards

### Wompi contract (official Colombia docs)

Sources:

- https://docs.wompi.co/en/docs/colombia/transacciones/
- https://docs.wompi.co/en/docs/colombia/widget-checkout-web/
- https://docs.wompi.co/docs/colombia/eventos/
- https://docs.wompi.co/en/docs/colombia/metodos-de-pago/

Verified:

- Amount: `amount_in_cents` (integer). Domain keeps `Money` decimal; adapter converts ×100 checked.
- Integrity: SHA256(`reference` + `amount_in_cents` + `currency` + integrity_secret)
- Webhook checksum: SHA256(concat `signature.properties` values + `timestamp` + events_secret); header `X-Event-Checksum`
- Statuses: PENDING, APPROVED, DECLINED, VOIDED, ERROR
- GET `/v1/transactions/{id}` authenticated **server-side with merchant PrivateKey** (`prv_*`). Public-key queries are no longer supported by Wompi and may return 404. **No official lookup-by-reference**
- Duplicate reference → HTTP 422
- MVP methods: Widget + CARD token (no PAN/CVV through YaaJuu). Widget returns client-safe parameters (`PublicKey`, `Reference`, `AmountInCents`, `Currency`, `IntegritySignature`); **no fabricated checkout URL**. PSE/Nequi excluded from MVP.
- Platform runtime `Payments:Wompi:Environment` (`Sandbox`|`Production`) selects the active merchant for **new** payments. Historical Attempts retain `MerchantConfigurationId` / `Environment` for webhook and reconciliation.

### Rejected alternatives

- Central YaaJuu Wompi account + later disbursement
- Split payment / platform wallet
- Client-confirmed payment (`mark-paid`)
- Inventory Commit on payment approval
- Webhook-only (no reconciliation)
- Blind POST retry of charge-creating calls
- Plaintext merchant secrets in PostgreSQL

### Known limitations / decisions pending

- Cancel while payment in-flight remains allowed (avoids Orders↔Payments circular dependency); late Approved → ReconciliationRequired (`OrderExpiredAfterPayment` / `OrderCancelledAfterPayment`)
- Provider-side idempotency: unique `reference` + local durable attempt; no official idempotency header used
- **No lookup-by-reference**: without `ProviderTransactionId`, crash window leaves Attempt `Unknown`, blocks new charges, and waits for webhook (or later reconciliation once an id exists). Timeout is never treated as Declined.
- Provider reference uniqueness is enforced locally (`ux_payment_attempts_merchant_reference`) and matches Wompi 422 on duplicate reference.
- Merchant rotation: historical Attempt keeps `MerchantConfigurationId` V1; new Attempts use active V2; webhook/reconciliation decrypts secrets from the Attempt's configuration.
- Amount/currency mismatch: apply **provider status first**. Only authentic `APPROVED` may mark Attempt/Payment Approved. Non-Approved + mismatch never fabricates Approved; may set `RequiresReconciliation`. Approved + mismatch: preserve external approval, set `RequiresReconciliation`, do **not** Confirm Order. Approved without amount/currency: reconciliation, no Confirm.
- Reconciliation failures (unexpected exceptions, decrypt failure, missing historical merchant) are logged at Error with safe identifiers while preserving per-attempt batch isolation.
- Automatic refunds, chargebacks, saved cards, PSE/Nequi, master-key rotation UI: future

### Hardening evidence (Phase 8.1 / 8.2)

IntegrationTests on PostgreSQL/Testcontainers cover multi-merchant routing, webhook signature/dedup/concurrency, same/different idempotency keys, Unknown crash window, reconciliation, late approvals, amount/currency mismatch (including non-Approved mismatch safety), inventory absolute boundary (no Commit on Approved), secret encryption-at-rest + API redaction, DB unique/FK constraints, PrivateKey lookup auth, explicit runtime Environment selection, Widget client-safe contract, and reconciliation observability.

## Consequences

Payments is reviewable as Phase 8. Production enablement still requires real merchant credentials, public webhook URL/TLS, and operational reconciliation process.
