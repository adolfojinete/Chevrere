using YaaJuu.Modules.Payments.Application;
using YaaJuu.Modules.Payments.Application.Abstractions;
using YaaJuu.Modules.Payments.Application.Commands;
using YaaJuu.Modules.Payments.Infrastructure.Persistence;
using YaaJuu.Modules.Payments.Infrastructure.Security;
using YaaJuu.Modules.Payments.Infrastructure.Wompi;
using YaaJuu.SharedKernel.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace YaaJuu.Modules.Payments.Infrastructure;

public static class PaymentsInfrastructureExtensions
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPaymentsApplication();
        services.AddOptions<PaymentsOptions>()
            .Bind(configuration.GetSection(PaymentsOptions.SectionName))
            .Validate(
                o => o.Wompi.TimeoutSeconds > 0
                     && o.Reconciliation.BatchSize is > 0 and <= 500
                     && o.Reconciliation.PollIntervalSeconds > 0,
                "Payments options must have positive Wompi timeout and reconciliation settings.")
            .ValidateOnStart();

        services.AddScoped<IPaymentStore, PaymentStore>();
        services.AddSingleton<IPaymentSecretProtector, AesGcmPaymentSecretProtector>();
        services.AddScoped<IPaymentProvider, WompiPaymentProvider>();

        services.AddHttpClient("Wompi")
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaymentsOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(opts.Wompi.TimeoutSeconds);
            });

        services.AddHostedService<PaymentReconciliationWorker>();
        return services;
    }
}

public sealed class PaymentReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    Microsoft.Extensions.Options.IOptions<PaymentsOptions> options,
    IHostEnvironment environment,
    Microsoft.Extensions.Logging.ILogger<PaymentReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing") || !options.Value.Reconciliation.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<ReconcilePaymentsHandler>();
                await handler.RunBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Payment reconciliation batch failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(options.Value.Reconciliation.PollIntervalSeconds), stoppingToken);
        }
    }
}
