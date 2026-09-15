namespace YaaJuu.Modules.Orders.Application;

public sealed class OrderReservationOptions
{
    public const string SectionName = "Orders";

    public int ReservationTtlMinutes { get; set; } = 15;

    public int ExpirationPollSeconds { get; set; } = 60;

    public int ExpirationBatchSize { get; set; } = 100;

    public bool ExpirationWorkerEnabled { get; set; } = true;
}
