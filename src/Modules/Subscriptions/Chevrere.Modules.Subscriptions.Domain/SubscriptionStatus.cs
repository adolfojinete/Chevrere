namespace Chevrere.Modules.Subscriptions.Domain;

public enum SubscriptionStatus
{
    Trial = 1,
    Active = 2,
    PastDue = 3,
    GracePeriod = 4,
    Suspended = 5,
    Cancelled = 6
}
