namespace YaaJuu.Modules.Orders.Domain;

public enum OrderStatus
{
    PendingPayment = 1,
    Confirmed = 2,
    Cancelled = 3,
    Expired = 4
}
