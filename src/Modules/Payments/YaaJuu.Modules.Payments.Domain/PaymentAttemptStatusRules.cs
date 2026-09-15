namespace YaaJuu.Modules.Payments.Domain;

public static class PaymentAttemptStatusRules
{
    public static readonly PaymentAttemptStatus[] InFlight =
    [
        PaymentAttemptStatus.Created,
        PaymentAttemptStatus.Initiating,
        PaymentAttemptStatus.Pending,
        PaymentAttemptStatus.Unknown
    ];

    public static bool IsInFlight(PaymentAttemptStatus status) =>
        status is PaymentAttemptStatus.Created
            or PaymentAttemptStatus.Initiating
            or PaymentAttemptStatus.Pending
            or PaymentAttemptStatus.Unknown;

    public static bool IsTerminalDecline(PaymentAttemptStatus status) =>
        status is PaymentAttemptStatus.Declined
            or PaymentAttemptStatus.Error
            or PaymentAttemptStatus.Voided;

    public static bool AllowsRetry(PaymentAttemptStatus status) => IsTerminalDecline(status);
}
