using YaaJuu.Modules.Consumer.Application.Commands;
using YaaJuu.Modules.Consumer.Application.Contracts;
using YaaJuu.Modules.Consumer.Application.Queries;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.Modules.Consumer.Application;

public static class ConsumerApplicationExtensions
{
    public static IServiceCollection AddConsumerApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(ConsumerApplicationExtensions).Assembly, includeInternalTypes: true);

        services.AddScoped<IHandler<ConfigureStoreServiceAreaCommand, Result<AdminServiceAreaDto>>, ConfigureStoreServiceAreaHandler>();
        services.AddScoped<IHandler<EnableStoreServiceAreaCommand, Result>, EnableStoreServiceAreaHandler>();
        services.AddScoped<IHandler<DisableStoreServiceAreaCommand, Result>, DisableStoreServiceAreaHandler>();

        services.AddScoped<IHandler<AdminGetServiceAreaQuery, Result<AdminServiceAreaDto>>, AdminGetServiceAreaHandler>();
        services.AddScoped<IHandler<BusinessGetServiceAreaQuery, Result<AdminServiceAreaDto>>, BusinessGetServiceAreaHandler>();

        services.AddScoped<IHandler<CheckCoverageQuery, Result<CoverageDto>>, CheckCoverageHandler>();
        services.AddScoped<IHandler<SearchConsumerCatalogQuery, Result<ConsumerCatalogResponse>>, SearchConsumerCatalogHandler>();
        services.AddScoped<IHandler<SearchConsumerCategoriesQuery, Result<ConsumerCategoriesResponse>>, SearchConsumerCategoriesHandler>();
        services.AddScoped<IHandler<GetConsumerProductDetailQuery, Result<ConsumerProductDetailDto>>, GetConsumerProductDetailHandler>();

        return services;
    }
}
