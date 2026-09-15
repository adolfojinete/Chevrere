using YaaJuu.Modules.Identity.Application.Contracts;
using YaaJuu.Modules.Identity.Application.Login;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.Modules.Identity.Application;

public static class IdentityApplicationExtensions
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(IdentityApplicationExtensions).Assembly, includeInternalTypes: true);
        services.AddScoped<IHandler<LoginRequest, Result<LoginResponse>>, LoginHandler>();
        return services;
    }
}
