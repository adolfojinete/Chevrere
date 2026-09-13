using Chevrere.Modules.Identity.Application.Contracts;
using Chevrere.Modules.Identity.Application.Login;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Modules.Identity.Application;

public static class IdentityApplicationExtensions
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(IdentityApplicationExtensions).Assembly, includeInternalTypes: true);
        services.AddScoped<IHandler<LoginRequest, Result<LoginResponse>>, LoginHandler>();
        return services;
    }
}
