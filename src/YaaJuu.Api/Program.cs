using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization;
using YaaJuu.Api.Middleware;
using YaaJuu.Infrastructure;
using YaaJuu.Infrastructure.Options;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Infrastructure.Seed;
using YaaJuu.Modules.Catalog.Infrastructure;
using YaaJuu.Modules.Consumer.Infrastructure;
using YaaJuu.Modules.Identity.Infrastructure;
using YaaJuu.Modules.Inventory.Infrastructure;
using YaaJuu.Modules.Orders.Infrastructure;
using YaaJuu.Modules.Payments.Infrastructure;
using YaaJuu.Modules.Pricing.Infrastructure;
using YaaJuu.Modules.Procurement.Infrastructure;
using YaaJuu.Modules.Subscriptions.Infrastructure;
using YaaJuu.Modules.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "YaaJuu.Api")
            .WriteTo.Console();
    });

    builder.Services.AddProblemDetails();
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "YaaJuu API",
            Version = "v1",
            Description = "Administración central de asociados, catálogo, pricing, inventario, abastecimiento, discovery del consumidor y pedidos."
        });
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Bearer. Ejemplo: Bearer {token}",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });
        options.AddSecurityDefinition("IdempotencyKey", new OpenApiSecurityScheme
        {
            Description = "Clave de idempotencia para mutaciones de inventario, abastecimiento y creación de pedidos (8-128 chars).",
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey
        });
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            }
        });
        options.OperationFilter<YaaJuu.Api.Swagger.IdempotencyKeyOperationFilter>();
    });

    builder.Services.AddYaaJuuInfrastructure(builder.Configuration);
    builder.Services.AddIdentityModule();
    builder.Services.AddTenancyModule();
    builder.Services.AddSubscriptionsModule();
    builder.Services.AddCatalogModule();
    builder.Services.AddPricingModule();
    builder.Services.AddInventoryModule();
    builder.Services.AddProcurementModule();
    builder.Services.AddConsumerModule();
    builder.Services.AddOrdersModule(builder.Configuration);
    builder.Services.AddPaymentsModule(builder.Configuration);

    var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
        ?? throw new InvalidOperationException("Jwt configuration is missing.");

    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });

    builder.Services.AddAuthorization();

    var connectionString = builder.Configuration.GetConnectionString("Database")
        ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

    builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString, name: "postgresql", tags: ["ready"]);

    var app = builder.Build();

    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("UserId", httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty);
        };
    });

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseMiddleware<CorrelationIdMiddleware>();

    if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false
    });
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "YaaJuu API v1");
        });
    }

    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Applying pending EF Core migrations");
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations are up to date");

        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync();
    }

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "YaaJuu API terminated unexpectedly");
    throw new InvalidOperationException("YaaJuu API terminated unexpectedly.", ex);
}
finally
{
    await Log.CloseAndFlushAsync();
}

[ExcludeFromCodeCoverage]
public partial class Program
{
    protected Program()
    {
    }
}
