namespace YaaJuu.Modules.Payments.Domain;

public enum PaymentProviderEventStatus
{
    Received = 1,
    Processed = 2,
    IgnoredDuplicate = 3,
    Failed = 4,
    Anomaly = 5
}
