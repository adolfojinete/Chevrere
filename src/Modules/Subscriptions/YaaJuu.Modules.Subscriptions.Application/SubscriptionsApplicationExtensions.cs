using YaaJuu.Modules.Subscriptions.Application.Commands;
using YaaJuu.Modules.Subscriptions.Application.Contracts;
using YaaJuu.Modules.Subscriptions.Application.Queries;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.Modules.Subscriptions.Application;

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
