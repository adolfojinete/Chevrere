using Chevrere.Modules.Orders.Application.Abstractions;
using Chevrere.Modules.Orders.Application.Commands;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Results;
using Chevrere.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chevrere.Modules.Orders.Infrastructure;

/// <summary>
/// Expires PendingPayment orders whose reservation window has elapsed. Disabled in Testing and when
/// <c>Orders:ExpirationWorkerEnabled</c> is false so tests drive expiration through the handler.
/// </summary>
public sealed class OrderExpirationWorker(
    IServiceScopeFactory scopes,
    IOrderReservationPolicy policy,
    IHostEnvironment environment,
    ILogger<OrderExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!policy.ExpirationWorkerEnabled || environment.IsEnvironment("Testing"))
        {
            logger.LogInformation("Order expiration worker is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error while expiring unpaid orders.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(policy.ExpirationPollSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ExpireBatchAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids;
        await using (var listScope = scopes.CreateAsyncScope())
        {
            var store = listScope.ServiceProvider.GetRequiredService<IOrderStore>();
            var clock = listScope.ServiceProvider.GetRequiredService<IClock>();
            ids = await store.ListExpiredPendingOrderIdsAsync(
                clock.UtcNow, policy.ExpirationBatchSize, cancellationToken);
        }

        foreach (var orderId in ids)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set(Guid.CreateVersion7().ToString("D"));
            var handler = scope.ServiceProvider.GetRequiredService<IHandler<ExpireOrderCommand, Result>>();
            try
            {
                var result = await handler.HandleAsync(new ExpireOrderCommand(orderId), cancellationToken);
                if (result.IsFailure)
                {
                    logger.LogError(
                        "Failed to expire order {OrderId}: {ErrorCode} {ErrorMessage}",
                        orderId,
                        result.Error!.Code,
                        result.Error.Message);
                }
            }
            catch (Exception ex) when (ex is DuplicateKeyException or ConcurrencyConflictException)
            {
                logger.LogDebug(ex, "Skipped concurrent expiration of order {OrderId}.", orderId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to expire order {OrderId}.", orderId);
            }
        }
    }
}
