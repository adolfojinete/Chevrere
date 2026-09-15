namespace YaaJuu.Modules.Payments.Domain;

public enum ReconciliationReason
{
    None = 0,
    OrderExpiredAfterPayment = 1,
    OrderCancelledAfterPayment = 2,
    AmountMismatch = 3,
    CurrencyMismatch = 4,
    DuplicateApproval = 5,
    UnexpectedProviderState = 6,
    OrderConfirmFailed = 7,
    MerchantMismatch = 8
}
