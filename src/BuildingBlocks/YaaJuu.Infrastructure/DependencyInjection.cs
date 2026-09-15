using YaaJuu.Infrastructure.Audit;
using YaaJuu.Infrastructure.Context;
using YaaJuu.Infrastructure.Idempotency;
using YaaJuu.Infrastructure.Identity;
using YaaJuu.Infrastructure.Options;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Infrastructure.Seed;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Idempotency;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddYaaJuuInfrastructure(
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

        services.AddDbContext<YaaJuuDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite());
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
            .AddEntityFrameworkStores<YaaJuuDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }
}
