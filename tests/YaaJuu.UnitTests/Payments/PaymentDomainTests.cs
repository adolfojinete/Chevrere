using YaaJuu.Modules.Payments.Domain;
using YaaJuu.SharedKernel.Domain;
using YaaJuu.SharedKernel.Domain.ValueObjects;

namespace YaaJuu.UnitTests.Payments;

public sealed class PaymentDomainTests
{
    private static readonly Guid OrderId = Guid.CreateVersion7();
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid MerchantId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_payment_freezes_order_total()
    {
        var payment = Payment.CreateForOrder(
            OrderId, TenantId, StoreId, Money.Create(50000m, "COP"), PaymentProvider.Wompi, Now);

        Assert.Equal(50000m, payment.Amount);
        Assert.Equal("COP", payment.Currency);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.False(payment.RequiresReconciliation);
    }

    [Fact]
    public void Declined_attempt_allows_second_attempt_and_preserves_history()
    {
        var payment = Payment.CreateForOrder(
            OrderId, TenantId, StoreId, Money.Create(50000m, "COP"), PaymentProvider.Wompi, Now);
        var first = payment.StartAttempt(MerchantId, MerchantEnvironment.Sandbox, "ref-1", Now);
        Assert.True(first.MarkDeclined(null, "DECLINED", "x", "declined", Now, null));

        var second = payment.StartAttempt(MerchantId, MerchantEnvironment.Sandbox, "ref-2", Now.AddMinutes(1));
        Assert.True(second.MarkApproved("tx-2", "APPROVED", Now.AddMinutes(1), null));
        Assert.True(payment.MarkApproved(second, Now.AddMinutes(1)));
        Assert.Equal(2, payment.Attempts.Count);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
    }

    [Fact]
    public void In_flight_attempt_blocks_new_attempt()
    {
        var payment = Payment.CreateForOrder(
            OrderId, TenantId, StoreId, Money.Create(50000m, "COP"), PaymentProvider.Wompi, Now);
        payment.StartAttempt(MerchantId, MerchantEnvironment.Sandbox, "ref-1", Now);
        var ex = Assert.Throws<DomainException>(() =>
            payment.StartAttempt(MerchantId, MerchantEnvironment.Sandbox, "ref-2", Now));
        Assert.Equal("payment.attempt.in_progress", ex.Code);
    }

    [Fact]
    public void Approved_is_monotonic_for_attempt()
    {
        var payment = Payment.CreateForOrder(
            OrderId, TenantId, StoreId, Money.Create(50000m, "COP"), PaymentProvider.Wompi, Now);
        var attempt = payment.StartAttempt(MerchantId, MerchantEnvironment.Sandbox, "ref-1", Now);
        Assert.True(attempt.MarkApproved("tx-1", "APPROVED", Now, null));
        Assert.False(attempt.MarkApproved("tx-1", "APPROVED", Now, null));
        Assert.False(attempt.MarkDeclined("tx-1", "DECLINED", null, null, Now, null));
        Assert.False(attempt.MarkPending("tx-1", "PENDING", null, Now));
    }

    [Fact]
    public void Late_approval_reconciliation_reason_can_be_set()
    {
        var payment = Payment.CreateForOrder(
            OrderId, TenantId, StoreId, Money.Create(50000m, "COP"), PaymentProvider.Wompi, Now);
        var attempt = payment.StartAttempt(MerchantId, MerchantEnvironment.Sandbox, "ref-1", Now);
        attempt.MarkApproved("tx-1", "APPROVED", Now, null);
        payment.MarkApproved(attempt, Now);
        Assert.True(payment.MarkRequiresReconciliation(ReconciliationReason.OrderExpiredAfterPayment, Now));
        Assert.True(payment.RequiresReconciliation);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
    }

    [Fact]
    public void Merchant_enable_requires_complete_secrets()
    {
        var cfg = PaymentMerchantConfiguration.Create(
            TenantId,
            PaymentProvider.Wompi,
            MerchantEnvironment.Sandbox,
            "pub_test",
            "enc-private",
            "enc-integrity",
            "enc-events",
            1,
            Now);
        Assert.True(cfg.Enable(Now));
        Assert.False(cfg.Enable(Now));
        Assert.True(cfg.Disable(Now));
        Assert.False(cfg.Disable(Now));
    }
}
