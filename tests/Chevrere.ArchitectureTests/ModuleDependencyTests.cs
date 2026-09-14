using Chevrere.Infrastructure;
using Chevrere.Modules.Catalog.Application;
using Chevrere.Modules.Catalog.Domain;
using Chevrere.Modules.Catalog.Infrastructure;
using Chevrere.Modules.Consumer.Application;
using Chevrere.Modules.Consumer.Domain;
using Chevrere.Modules.Consumer.Infrastructure;
using Chevrere.Modules.Identity.Application;
using Chevrere.Modules.Identity.Domain;
using Chevrere.Modules.Identity.Infrastructure;
using Chevrere.Modules.Inventory.Application;
using Chevrere.Modules.Inventory.Domain;
using Chevrere.Modules.Inventory.Infrastructure;
using Chevrere.Modules.Pricing.Application;
using Chevrere.Modules.Pricing.Domain;
using Chevrere.Modules.Pricing.Infrastructure;
using Chevrere.Modules.Procurement.Application;
using Chevrere.Modules.Procurement.Domain;
using Chevrere.Modules.Procurement.Infrastructure;
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
            typeof(InventoryItem).Assembly,
            typeof(PurchaseOrder).Assembly,
            typeof(StoreServiceArea).Assembly,
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
                    "Chevrere.Modules.Pricing.Infrastructure",
                    "Chevrere.Modules.Inventory.Application",
                    "Chevrere.Modules.Inventory.Infrastructure",
                    "Chevrere.Modules.Procurement.Application",
                    "Chevrere.Modules.Procurement.Infrastructure",
                    "Chevrere.Modules.Consumer.Application",
                    "Chevrere.Modules.Consumer.Infrastructure")
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
            typeof(PricingApplicationExtensions).Assembly,
            typeof(InventoryApplicationExtensions).Assembly,
            typeof(ProcurementApplicationExtensions).Assembly,
            typeof(ConsumerApplicationExtensions).Assembly
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
                    "Chevrere.Modules.Pricing.Infrastructure",
                    "Chevrere.Modules.Inventory.Infrastructure",
                    "Chevrere.Modules.Procurement.Infrastructure",
                    "Chevrere.Modules.Consumer.Infrastructure")
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
    public void Inventory_domain_does_not_depend_on_catalog_pricing_or_tenancy()
    {
        var result = Types.InAssembly(typeof(InventoryItem).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Chevrere.Api",
                "Chevrere.Infrastructure",
                "Chevrere.Modules.Catalog.Application",
                "Chevrere.Modules.Catalog.Domain",
                "Chevrere.Modules.Catalog.Infrastructure",
                "Chevrere.Modules.Pricing.Application",
                "Chevrere.Modules.Pricing.Domain",
                "Chevrere.Modules.Pricing.Infrastructure",
                "Chevrere.Modules.Tenancy.Application",
                "Chevrere.Modules.Tenancy.Domain",
                "Chevrere.Modules.Tenancy.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
    }

    [Fact]
    public void Inventory_application_does_not_depend_on_catalog_pricing_or_tenancy_modules()
    {
        var result = Types.InAssembly(typeof(InventoryApplicationExtensions).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Chevrere.Api",
                "Chevrere.Infrastructure",
                "Chevrere.Modules.Catalog.Application",
                "Chevrere.Modules.Catalog.Domain",
                "Chevrere.Modules.Catalog.Infrastructure",
                "Chevrere.Modules.Pricing.Application",
                "Chevrere.Modules.Pricing.Domain",
                "Chevrere.Modules.Pricing.Infrastructure",
                "Chevrere.Modules.Tenancy.Application",
                "Chevrere.Modules.Tenancy.Domain",
                "Chevrere.Modules.Tenancy.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
    }

    [Fact]
    public void Procurement_domain_does_not_depend_on_other_modules()
    {
        var result = Types.InAssembly(typeof(PurchaseOrder).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Chevrere.Api",
                "Chevrere.Infrastructure",
                "Chevrere.Modules.Catalog.Application",
                "Chevrere.Modules.Catalog.Domain",
                "Chevrere.Modules.Catalog.Infrastructure",
                "Chevrere.Modules.Pricing.Application",
                "Chevrere.Modules.Pricing.Domain",
                "Chevrere.Modules.Pricing.Infrastructure",
                "Chevrere.Modules.Inventory.Application",
                "Chevrere.Modules.Inventory.Domain",
                "Chevrere.Modules.Inventory.Infrastructure",
                "Chevrere.Modules.Tenancy.Application",
                "Chevrere.Modules.Tenancy.Domain",
                "Chevrere.Modules.Tenancy.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
    }

    /// <summary>
    /// Procurement moves stock through the SharedKernel inbound contract only. Taking a reference to
    /// Inventory, Catalog or Pricing would turn two modules into one.
    /// </summary>
    [Fact]
    public void Procurement_application_reaches_inventory_only_through_shared_kernel()
    {
        var result = Types.InAssembly(typeof(ProcurementApplicationExtensions).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Chevrere.Api",
                "Chevrere.Infrastructure",
                "Chevrere.Modules.Catalog.Application",
                "Chevrere.Modules.Catalog.Domain",
                "Chevrere.Modules.Catalog.Infrastructure",
                "Chevrere.Modules.Pricing.Application",
                "Chevrere.Modules.Pricing.Domain",
                "Chevrere.Modules.Pricing.Infrastructure",
                "Chevrere.Modules.Inventory.Application",
                "Chevrere.Modules.Inventory.Domain",
                "Chevrere.Modules.Inventory.Infrastructure",
                "Chevrere.Modules.Tenancy.Application",
                "Chevrere.Modules.Tenancy.Domain",
                "Chevrere.Modules.Tenancy.Infrastructure")
            .GetResult();

        var usesContract = Types.InAssembly(typeof(ProcurementApplicationExtensions).Assembly)
            .That()
            .HaveNameEndingWith("ReceiveGoodsHandler")
            .Should()
            .HaveDependencyOn("Chevrere.SharedKernel.Inventory")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
        Assert.True(usesContract.IsSuccessful, Format(usesContract));
    }

    /// <summary>
    /// Inventory must not learn about its callers: the inbound contract lives in SharedKernel.
    /// </summary>
    [Fact]
    public void Inventory_does_not_depend_on_procurement()
    {
        var assemblies = new[]
        {
            typeof(InventoryItem).Assembly,
            typeof(InventoryApplicationExtensions).Assembly,
            typeof(InventoryInfrastructureExtensions).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(
                    "Chevrere.Modules.Procurement.Application",
                    "Chevrere.Modules.Procurement.Domain",
                    "Chevrere.Modules.Procurement.Infrastructure")
                .GetResult();

            Assert.True(result.IsSuccessful, Format(result));
        }
    }

    /// <summary>
    /// Consumer Discovery composes Catalog, Pricing, Inventory and Tenancy, but only in Infrastructure.
    /// A reference from its Domain or Application would turn the consumer surface into a second copy
    /// of the whole back office.
    /// </summary>
    [Fact]
    public void Consumer_domain_and_application_do_not_depend_on_other_modules()
    {
        var assemblies = new[]
        {
            typeof(StoreServiceArea).Assembly,
            typeof(ConsumerApplicationExtensions).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(
                    "Chevrere.Api",
                    "Chevrere.Infrastructure",
                    "Chevrere.Modules.Catalog.Application",
                    "Chevrere.Modules.Catalog.Domain",
                    "Chevrere.Modules.Catalog.Infrastructure",
                    "Chevrere.Modules.Pricing.Application",
                    "Chevrere.Modules.Pricing.Domain",
                    "Chevrere.Modules.Pricing.Infrastructure",
                    "Chevrere.Modules.Inventory.Application",
                    "Chevrere.Modules.Inventory.Domain",
                    "Chevrere.Modules.Inventory.Infrastructure",
                    "Chevrere.Modules.Procurement.Application",
                    "Chevrere.Modules.Procurement.Domain",
                    "Chevrere.Modules.Procurement.Infrastructure",
                    "Chevrere.Modules.Tenancy.Application",
                    "Chevrere.Modules.Tenancy.Domain",
                    "Chevrere.Modules.Tenancy.Infrastructure")
                .GetResult();

            Assert.True(result.IsSuccessful, Format(result));
        }
    }

    /// <summary>
    /// NetTopologySuite is a persistence detail. The domain reasons about latitude and longitude.
    /// </summary>
    [Fact]
    public void Consumer_domain_does_not_depend_on_spatial_or_persistence_libraries()
    {
        var result = Types.InAssembly(typeof(StoreServiceArea).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "NetTopologySuite",
                "Npgsql",
                "Microsoft.EntityFrameworkCore")
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
            typeof(PricingInfrastructureExtensions).Assembly,
            typeof(InventoryInfrastructureExtensions).Assembly,
            typeof(ProcurementInfrastructureExtensions).Assembly,
            typeof(ConsumerInfrastructureExtensions).Assembly
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
