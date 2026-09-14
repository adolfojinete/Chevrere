using Chevrere.Modules.Orders.Application;
using Chevrere.Modules.Orders.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Chevrere.Modules.Orders.Infrastructure;

public sealed class ConfiguredOrderReservationPolicy(IOptions<OrderReservationOptions> options) : IOrderReservationPolicy
{
    public TimeSpan ReservationTtl => TimeSpan.FromMinutes(options.Value.ReservationTtlMinutes);

    public int ExpirationPollSeconds => options.Value.ExpirationPollSeconds;

    public int ExpirationBatchSize => options.Value.ExpirationBatchSize;

    public bool ExpirationWorkerEnabled => options.Value.ExpirationWorkerEnabled;
}
