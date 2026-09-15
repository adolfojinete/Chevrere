using YaaJuu.SharedKernel.Domain;
using YaaJuu.SharedKernel.Domain.ValueObjects;

namespace YaaJuu.Modules.Payments.Domain;

/// <summary>
/// Intention to collect payment for exactly one Order. Attempts hold provider interactions.
/// </summary>
public sealed class Payment : AggregateRoot
{
    private readonly List<PaymentAttempt> _attempts = [];

    private Payment()
    {
        Currency = null!;
    }

    public Guid OrderId { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public PaymentProvider Provider { get; private set; }

    public PaymentStatus Status { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public bool RequiresReconciliation { get; private set; }

    public ReconciliationReason ReconciliationReason { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public IReadOnlyList<PaymentAttempt> Attempts => _attempts;

    public static Payment CreateForOrder(
        Guid orderId,
        Guid tenantId,
        Guid storeId,
        Money total,
        PaymentProvider provider,
        DateTimeOffset utcNow)
    {
        if (orderId == Guid.Empty || tenantId == Guid.Empty || storeId == Guid.Empty)
        {
            throw new DomainException("payment.identity.required", "Payment requires order, tenant and store.");
        }

        if (!string.Equals(total.Currency, CurrencyCodes.Cop, StringComparison.Ordinal))
        {
            throw new DomainException("payment.currency.unsupported", "Payments only accept COP.");
        }

        return new Payment
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            TenantId = tenantId,
            StoreId = storeId,
            Provider = provider,
            Status = PaymentStatus.Pending,
            Amount = total.Amount,
            Currency = total.Currency,
            RequiresReconciliation = false,
            ReconciliationReason = ReconciliationReason.None,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public PaymentAttempt StartAttempt(
        Guid merchantConfigurationId,
        MerchantEnvironment environment,
        string merchantReference,
        DateTimeOffset utcNow)
    {
        if (Status == PaymentStatus.Approved)
        {
            throw new DomainException(
                "payment.already_approved",
                "An approved payment cannot start another attempt.");
        }

        if (RequiresReconciliation)
        {
            throw new DomainException(
                "payment.reconciliation.required",
                "A payment requiring reconciliation cannot start another attempt.");
        }

        if (_attempts.Any(a => a.IsInFlight))
        {
            throw new DomainException(
                "payment.attempt.in_progress",
                "An in-flight payment attempt already exists.");
        }

        var money = Money.FromPersistence(Amount, Currency);
        var attempt = PaymentAttempt.Create(
            Id,
            merchantConfigurationId,
            Provider,
            environment,
            money,
            merchantReference,
            utcNow);
        _attempts.Add(attempt);
        UpdatedAt = utcNow;
        return attempt;
    }

    /// <returns>true when Payment moved to Approved.</returns>
    public bool MarkApproved(PaymentAttempt attempt, DateTimeOffset utcNow)
    {
        EnsureOwns(attempt);
        if (Status == PaymentStatus.Approved && !RequiresReconciliation)
        {
            return false;
        }

        var approvedCount = _attempts.Count(a => a.Status == PaymentAttemptStatus.Approved);
        if (approvedCount > 1)
        {
            MarkRequiresReconciliation(ReconciliationReason.DuplicateApproval, utcNow);
            if (Status != PaymentStatus.Approved)
            {
                Status = PaymentStatus.Approved;
                ApprovedAt ??= utcNow;
                UpdatedAt = utcNow;
                return true;
            }

            return false;
        }

        if (Status == PaymentStatus.Approved)
        {
            return false;
        }

        Status = PaymentStatus.Approved;
        ApprovedAt = utcNow;
        UpdatedAt = utcNow;
        return true;
    }

    public bool MarkRequiresReconciliation(ReconciliationReason reason, DateTimeOffset utcNow)
    {
        if (reason == ReconciliationReason.None)
        {
            throw new DomainException(
                "payment.reconciliation.reason_required",
                "Reconciliation reason is required.");
        }

        if (RequiresReconciliation && ReconciliationReason == reason)
        {
            return false;
        }

        RequiresReconciliation = true;
        ReconciliationReason = reason;
        if (Status != PaymentStatus.Approved)
        {
            Status = PaymentStatus.RequiresReconciliation;
        }

        UpdatedAt = utcNow;
        return true;
    }

    public PaymentAttempt? GetInFlightAttempt() =>
        _attempts.FirstOrDefault(a => a.IsInFlight);

    public PaymentAttempt? GetLatestAttempt() =>
        _attempts.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).FirstOrDefault();

    public bool CanStartNewAttempt() =>
        Status != PaymentStatus.Approved
        && !RequiresReconciliation
        && !_attempts.Any(a => a.IsInFlight)
        && (_attempts.Count == 0 || _attempts.Any(a => PaymentAttemptStatusRules.AllowsRetry(a.Status)));

    private void EnsureOwns(PaymentAttempt attempt)
    {
        if (attempt.PaymentId != Id || !_attempts.Any(a => a.Id == attempt.Id))
        {
            throw new DomainException("payment.attempt.foreign", "Attempt does not belong to this payment.");
        }
    }
}
