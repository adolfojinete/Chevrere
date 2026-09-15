using YaaJuu.Modules.Subscriptions.Application;
using YaaJuu.Modules.Subscriptions.Application.Abstractions;
using YaaJuu.Modules.Subscriptions.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.Modules.Subscriptions.Infrastructure;

public static class SubscriptionsInfrastructureExtensions
{
    public static IServiceCollection AddSubscriptionsModule(this IServiceCollection services)
    {
        services.AddSubscriptionsApplication();
        services.AddScoped<IPlanReader, PlanReader>();
        services.AddScoped<ISubscriptionReader, SubscriptionReader>();
        services.AddScoped<ISubscriptionProvisioning, SubscriptionProvisioning>();
        return services;
    }
}
