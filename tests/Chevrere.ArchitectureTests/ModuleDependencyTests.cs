using Chevrere.Infrastructure;
using Chevrere.Modules.Catalog.Application;
using Chevrere.Modules.Catalog.Domain;
using Chevrere.Modules.Catalog.Infrastructure;
using Chevrere.Modules.Identity.Application;
using Chevrere.Modules.Identity.Domain;
using Chevrere.Modules.Identity.Infrastructure;
using Chevrere.Modules.Pricing.Application;
using Chevrere.Modules.Pricing.Domain;
using Chevrere.Modules.Pricing.Infrastructure;
using Chevrere.Modules.Subscriptions.Application;
using Chevrere.Modules.Subscriptions.Domain;
using Chevrere.Modules.Subscriptions.Infrastructure;
using Chevrere.Modules.Tenancy.Application;
using Chevrere.Modules.Tenancy.Domain;
using Chevrere.Modules.Tenancy.Infrastructure;
using Chevrere.SharedKernel.Domain;
using NetArchTest.Rules;

namespace Chevrere.ArchitectureTests;

public sealed class ModuleDependencyTests
{
    [Fact]
    public void Domain_does_not_depend_on_application_infrastructure_or_api()
    {
        var assemblies = new[]
        {
            typeof(RoleNames).Assembly,
            typeof(Tenant).Assembly,
            typeof(Plan).Assembly,
            typeof(Category).Assembly,
            typeof(GlobalProductPrice).Assembly,
            typeof(Entity).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(
                    "Chevrere.Api",
                    "Chevrere.Infrastructure",
                    "Chevrere.Modules.Identity.Application",
                    "Chevrere.Modules.Identity.Infrastructure",
                    "Chevrere.Modules.Tenancy.Application",
                    "Chevrere.Modules.Tenancy.Infrastructure",
                    "Chevrere.Modules.Subscriptions.Application",
                    "Chevrere.Modules.Subscriptions.Infrastructure",
                    "Chevrere.Modules.Catalog.Application",
                    "Chevrere.Modules.Catalog.Infrastructure",
                    "Chevrere.Modules.Pricing.Application",
                    "Chevrere.Modules.Pricing.Infrastructure")
                .GetResult();

            Assert.True(result.IsSuccessful, Format(result));
        }
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_api()
    {
        var assemblies = new[]
        {
            typeof(IdentityApplicationExtensions).Assembly,
            typeof(TenancyApplicationExtensions).Assembly,
            typeof(SubscriptionsApplicationExtensions).Assembly,
            typeof(CatalogApplicationExtensions).Assembly,
            typeof(PricingApplicationExtensions).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(
                    "Chevrere.Api",
                    "Chevrere.Infrastructure",
                    "Chevrere.Modules.Identity.Infrastructure",
                    "Chevrere.Modules.Tenancy.Infrastructure",
                    "Chevrere.Modules.Subscriptions.Infrastructure",
                    "Chevrere.Modules.Catalog.Infrastructure",
                    "Chevrere.Modules.Pricing.Infrastructure")
                .GetResult();

            Assert.True(result.IsSuccessful, Format(result));
        }
    }

    [Fact]
    public void Shared_infrastructure_does_not_depend_on_api()
    {
        var result = Types.InAssembly(typeof(DependencyInjection).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Chevrere.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
    }

    [Fact]
    public void Identity_and_subscriptions_application_do_not_depend_on_tenancy()
    {
        var identity = Types.InAssembly(typeof(IdentityApplicationExtensions).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Chevrere.Modules.Tenancy.Application",
                "Chevrere.Modules.Tenancy.Domain")
            .GetResult();

        var subscriptions = Types.InAssembly(typeof(SubscriptionsApplicationExtensions).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Chevrere.Modules.Tenancy.Application",
                "Chevrere.Modules.Tenancy.Domain",
                "Chevrere.Modules.Identity.Application")
            .GetResult();

        Assert.True(identity.IsSuccessful, Format(identity));
        Assert.True(subscriptions.IsSuccessful, Format(subscriptions));
    }

    [Fact]
    public void Catalog_application_does_not_depend_on_tenancy()
    {
        var result = Types.InAssembly(typeof(CatalogApplicationExtensions).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Chevrere.Modules.Tenancy.Application",
                "Chevrere.Modules.Tenancy.Domain",
                "Chevrere.Modules.Tenancy.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
    }

    [Fact]
    public void Pricing_application_does_not_depend_on_catalog_or_tenancy_modules()
    {
        var result = Types.InAssembly(typeof(PricingApplicationExtensions).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Chevrere.Modules.Catalog.Application",
                "Chevrere.Modules.Catalog.Domain",
                "Chevrere.Modules.Catalog.Infrastructure",
                "Chevrere.Modules.Tenancy.Application",
                "Chevrere.Modules.Tenancy.Domain",
                "Chevrere.Modules.Tenancy.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
    }

    [Fact]
    public void Module_infrastructure_does_not_depend_on_api()
    {
        var assemblies = new[]
        {
            typeof(IdentityInfrastructureExtensions).Assembly,
            typeof(TenancyInfrastructureExtensions).Assembly,
            typeof(SubscriptionsInfrastructureExtensions).Assembly,
            typeof(CatalogInfrastructureExtensions).Assembly,
            typeof(PricingInfrastructureExtensions).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn("Chevrere.Api")
                .GetResult();

            Assert.True(result.IsSuccessful, Format(result));
        }
    }

    private static string Format(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : string.Join(Environment.NewLine, result.FailingTypeNames ?? []);
}
