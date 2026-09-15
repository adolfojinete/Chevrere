using YaaJuu.Modules.Payments.Application.Commands;
using YaaJuu.Modules.Payments.Application.Contracts;
using YaaJuu.Modules.Payments.Application.Queries;
using YaaJuu.Modules.Payments.Application.Services;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.Modules.Payments.Application;

public static class PaymentsApplicationExtensions
{
    public static IServiceCollection AddPaymentsApplication(this IServiceCollection services)
    {
        services.AddScoped<ProviderStatusProcessor>();
        services.AddScoped<ReconcilePaymentsHandler>();
        services.AddScoped<IHandler<InitializePaymentCommand, Result<ConsumerPaymentDto>>, InitializePaymentHandler>();
        services.AddScoped<IHandler<ProcessWompiWebhookCommand, Result<WebhookProcessOutcome>>, ProcessWompiWebhookHandler>();
        services.AddScoped<IHandler<ConfigureWompiMerchantCommand, Result<WompiMerchantConfigurationDto>>, ConfigureWompiMerchantHandler>();
        services.AddScoped<IHandler<EnableWompiMerchantCommand, Result<WompiMerchantConfigurationDto>>, EnableWompiMerchantHandler>();
        services.AddScoped<IHandler<DisableWompiMerchantCommand, Result<WompiMerchantConfigurationDto>>, DisableWompiMerchantHandler>();
        services.AddScoped<IHandler<GetConsumerPaymentQuery, Result<ConsumerPaymentDto>>, GetConsumerPaymentHandler>();
        services.AddScoped<IHandler<GetAdminMerchantQuery, Result<WompiMerchantConfigurationDto>>, GetAdminMerchantHandler>();
        services.AddScoped<IHandler<GetBusinessPaymentConfigurationQuery, Result<BusinessPaymentConfigurationStatusDto>>, GetBusinessPaymentConfigurationHandler>();
        services.AddScoped<IHandler<ListBusinessPaymentsQuery, Result<PagedResult<BusinessPaymentSummaryDto>>>, ListBusinessPaymentsHandler>();
        services.AddScoped<IHandler<GetBusinessPaymentQuery, Result<BusinessPaymentDetailDto>>, GetBusinessPaymentHandler>();
        services.AddScoped<IHandler<ListAdminPaymentsQuery, Result<PagedResult<AdminPaymentSummaryDto>>>, ListAdminPaymentsHandler>();
        services.AddScoped<IHandler<GetAdminPaymentQuery, Result<AdminPaymentDetailDto>>, GetAdminPaymentHandler>();
        return services;
    }
}
