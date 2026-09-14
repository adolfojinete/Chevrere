using Chevrere.Infrastructure.Audit;
using Chevrere.Infrastructure.Context;
using Chevrere.Infrastructure.Idempotency;
using Chevrere.Infrastructure.Identity;
using Chevrere.Infrastructure.Options;
using Chevrere.Infrastructure.Persistence;
using Chevrere.Infrastructure.Seed;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Idempotency;
using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chevrere.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddChevrereInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IClock, SystemClock>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<ICorrelationContext, CorrelationContext>();
        services.AddScoped<ITenantFilterBypass, TenantFilterBypass>();
        services.AddScoped<IAuditRecorder, AuditRecorder>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<DatabaseSeeder>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<BootstrapOptions>().Bind(configuration.GetSection(BootstrapOptions.SectionName));
        services.AddOptions<SubscriptionOptions>().Bind(configuration.GetSection(SubscriptionOptions.SectionName));
        services.AddOptions<PlanSeedOptions>().Bind(configuration.GetSection(PlanSeedOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

        services.AddDbContext<ChevrereDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            options.UseSnakeCaseNamingConvention();
        });

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 8;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ChevrereDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }
}
