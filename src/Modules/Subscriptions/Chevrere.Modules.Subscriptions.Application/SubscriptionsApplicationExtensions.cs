using Chevrere.Modules.Subscriptions.Application.Commands;
using Chevrere.Modules.Subscriptions.Application.Contracts;
using Chevrere.Modules.Subscriptions.Application.Queries;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Modules.Subscriptions.Application;

public static class SubscriptionsApplicationExtensions
{
    public static IServiceCollection AddSubscriptionsApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(SubscriptionsApplicationExtensions).Assembly, includeInternalTypes: true);
        services.AddScoped<IHandler<ListPlansQuery, IReadOnlyList<PlanDto>>, ListPlansHandler>();
        services.AddScoped<IHandler<GetSubscriptionByTenantQuery, Result<SubscriptionDto>>, GetSubscriptionByTenantHandler>();
        services.AddScoped<IHandler<ChangePlanCommand, Result>, ChangePlanHandler>();
        return services;
    }
}
