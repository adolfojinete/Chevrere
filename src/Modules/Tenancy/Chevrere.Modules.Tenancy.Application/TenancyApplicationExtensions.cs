using Chevrere.Modules.Tenancy.Application.Commands.ActivateFranchisee;
using Chevrere.Modules.Tenancy.Application.Commands.CreateFranchisee;
using Chevrere.Modules.Tenancy.Application.Commands.ReactivateFranchisee;
using Chevrere.Modules.Tenancy.Application.Commands.SuspendFranchisee;
using Chevrere.Modules.Tenancy.Application.Contracts;
using Chevrere.Modules.Tenancy.Application.Queries;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Modules.Tenancy.Application;

public static class TenancyApplicationExtensions
{
    public static IServiceCollection AddTenancyApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(TenancyApplicationExtensions).Assembly, includeInternalTypes: true);
        services.AddScoped<IHandler<CreateFranchiseeRequest, Result<CreateFranchiseeResponse>>, CreateFranchiseeHandler>();
        services.AddScoped<IHandler<ActivateFranchiseeCommand, Result>, ActivateFranchiseeHandler>();
        services.AddScoped<IHandler<SuspendFranchiseeCommand, Result>, SuspendFranchiseeHandler>();
        services.AddScoped<IHandler<ReactivateFranchiseeCommand, Result>, ReactivateFranchiseeHandler>();
        services.AddScoped<IHandler<GetFranchiseeQuery, Result<FranchiseeDetailDto>>, GetFranchiseeHandler>();
        services.AddScoped<IHandler<FranchiseeListQuery, PagedResult<FranchiseeListItemDto>>, ListFranchiseesHandler>();
        services.AddScoped<IHandler<ListFranchiseeStoresQuery, Result<IReadOnlyList<StoreDto>>>, ListFranchiseeStoresHandler>();
        services.AddScoped<IHandler<ListAuditEventsQuery, IReadOnlyList<AuditEventDto>>, ListAuditEventsHandler>();
        services.AddScoped<IHandler<ListBusinessStoresQuery, Result<IReadOnlyList<StoreDto>>>, ListBusinessStoresHandler>();
        return services;
    }
}
