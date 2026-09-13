using Chevrere.Modules.Subscriptions.Application;
using Chevrere.Modules.Subscriptions.Application.Abstractions;
using Chevrere.Modules.Subscriptions.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Modules.Subscriptions.Infrastructure;

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
