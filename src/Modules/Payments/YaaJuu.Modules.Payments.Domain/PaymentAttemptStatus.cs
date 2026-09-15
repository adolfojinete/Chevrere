namespace YaaJuu.Modules.Payments.Domain;

public enum PaymentAttemptStatus
{
    Created = 1,
    Initiating = 2,
    Pending = 3,
    Approved = 4,
    Declined = 5,
    Unknown = 6,
    Error = 7,
    Voided = 8
}
