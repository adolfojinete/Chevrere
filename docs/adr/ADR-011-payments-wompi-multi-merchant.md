# ADR-011 — Payments + Wompi multi-comercio

- Status: Accepted
- Date: 2026-09-15
- Wompi docs consulted: 2026-09-14 / 2026-09-15

## Context

YaaJuu must collect consumer Order payments without becoming the central merchant of record. Each associate (Tenant) operates their own Wompi commerce credentials. Orders already create `PendingPayment` with an Active inventory reservation; payment success must Confirm the Order without committing inventory.

## Decision

Introduce bounded context `Payments` (Domain / Application / Infrastructure) with:

- `Payment` (1 per Order) + `PaymentAttempt` (N)
- Tenant-scoped versioned `PaymentMerchantConfiguration` (Wompi)
- AES-256-GCM secret protection (`YAAJUU_PAYMENT_SECRETS_KEY` / `Payments:SecretsMasterKey`)
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
- GET `/v1/transactions/{id}` with **public** key. **No official lookup-by-reference**
- Duplicate reference → HTTP 422
- MVP methods: Widget + CARD token (no PAN/CVV through YaaJuu). PSE/Nequi excluded from MVP.

### Rejected alternatives

- Central YaaJuu Wompi account + later disbursement
- Split payment / platform wallet
- Client-confirmed payment (`mark-paid`)
- Inventory Commit on payment approval
- Webhook-only (no reconciliation)
- Blind POST retry of charge-creating calls
- Plaintext merchant secrets in PostgreSQL

### Known limitations / decisions pending

- Cancel while payment in-flight remains allowed (avoids Orders↔Payments circular dependency); late Approved → ReconciliationRequired
- Provider-side idempotency: unique `reference` + local durable attempt; no official idempotency header used
- Without `ProviderTransactionId`, recovery after crash depends on webhook (no lookup-by-reference)
- Automatic refunds, chargebacks, saved cards, PSE/Nequi, master-key rotation UI: future

## Consequences

Payments is reviewable as Phase 8. Production enablement still requires real merchant credentials, public webhook URL/TLS, and operational reconciliation process.
