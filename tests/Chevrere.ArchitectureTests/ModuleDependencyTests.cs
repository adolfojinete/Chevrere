using Chevrere.Infrastructure;
using Chevrere.Modules.Identity.Application;
using Chevrere.Modules.Identity.Domain;
using Chevrere.Modules.Identity.Infrastructure;
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
                    "Chevrere.Modules.Subscriptions.Infrastructure")
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
            typeof(SubscriptionsApplicationExtensions).Assembly
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
                    "Chevrere.Modules.Subscriptions.Infrastructure")
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
    public void Module_infrastructure_does_not_depend_on_api()
    {
        var assemblies = new[]
        {
            typeof(IdentityInfrastructureExtensions).Assembly,
            typeof(TenancyInfrastructureExtensions).Assembly,
            typeof(SubscriptionsInfrastructureExtensions).Assembly
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
