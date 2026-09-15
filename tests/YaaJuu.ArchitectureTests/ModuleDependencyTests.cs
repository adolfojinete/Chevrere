using YaaJuu.Infrastructure;
using YaaJuu.Modules.Catalog.Application;
using YaaJuu.Modules.Catalog.Domain;
using YaaJuu.Modules.Catalog.Infrastructure;
using YaaJuu.Modules.Consumer.Application;
using YaaJuu.Modules.Consumer.Domain;
using YaaJuu.Modules.Consumer.Infrastructure;
using YaaJuu.Modules.Identity.Application;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Identity.Infrastructure;
using YaaJuu.Modules.Inventory.Application;
using YaaJuu.Modules.Inventory.Domain;
using YaaJuu.Modules.Inventory.Infrastructure;
using YaaJuu.Modules.Pricing.Application;
using YaaJuu.Modules.Pricing.Domain;
using YaaJuu.Modules.Pricing.Infrastructure;
using YaaJuu.Modules.Procurement.Application;
using YaaJuu.Modules.Procurement.Domain;
using YaaJuu.Modules.Procurement.Infrastructure;
using YaaJuu.Modules.Subscriptions.Application;
using YaaJuu.Modules.Subscriptions.Domain;
using YaaJuu.Modules.Subscriptions.Infrastructure;
using YaaJuu.Modules.Tenancy.Application;
using YaaJuu.Modules.Tenancy.Domain;
using YaaJuu.Modules.Tenancy.Infrastructure;
using YaaJuu.SharedKernel.Domain;
using NetArchTest.Rules;

namespace YaaJuu.ArchitectureTests;

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
            typeof(YaaJuu.Modules.Orders.Domain.Order).Assembly,
            typeof(YaaJuu.Modules.Payments.Domain.Payment).Assembly,
            typeof(StoreServiceArea).Assembly,
            typeof(Entity).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(
                    "YaaJuu.Api",
                    "YaaJuu.Infrastructure",
                    "YaaJuu.Modules.Identity.Application",
                    "YaaJuu.Modules.Identity.Infrastructure",
                    "YaaJuu.Modules.Tenancy.Application",
                    "YaaJuu.Modules.Tenancy.Infrastructure",
                    "YaaJuu.Modules.Subscriptions.Application",
                    "YaaJuu.Modules.Subscriptions.Infrastructure",
                    "YaaJuu.Modules.Catalog.Application",
                    "YaaJuu.Modules.Catalog.Infrastructure",
                    "YaaJuu.Modules.Pricing.Application",
                    "YaaJuu.Modules.Pricing.Infrastructure",
                    "YaaJuu.Modules.Inventory.Application",
                    "YaaJuu.Modules.Inventory.Infrastructure",
                    "YaaJuu.Modules.Procurement.Application",
                    "YaaJuu.Modules.Procurement.Infrastructure",
                    "YaaJuu.Modules.Consumer.Application",
                    "YaaJuu.Modules.Consumer.Infrastructure",
                    "YaaJuu.Modules.Orders.Application",
                    "YaaJuu.Modules.Orders.Infrastructure",
                    "YaaJuu.Modules.Payments.Application",
                    "YaaJuu.Modules.Payments.Infrastructure")
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
            typeof(ConsumerApplicationExtensions).Assembly,
            typeof(YaaJuu.Modules.Orders.Application.OrdersApplicationExtensions).Assembly,
            typeof(YaaJuu.Modules.Payments.Application.PaymentsApplicationExtensions).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(
                    "YaaJuu.Api",
                    "YaaJuu.Infrastructure",
                    "YaaJuu.Modules.Identity.Infrastructure",
                    "YaaJuu.Modules.Tenancy.Infrastructure",
                    "YaaJuu.Modules.Subscriptions.Infrastructure",
                    "YaaJuu.Modules.Catalog.Infrastructure",
                    "YaaJuu.Modules.Pricing.Infrastructure",
                    "YaaJuu.Modules.Inventory.Infrastructure",
                    "YaaJuu.Modules.Procurement.Infrastructure",
                    "YaaJuu.Modules.Consumer.Infrastructure",
                    "YaaJuu.Modules.Orders.Infrastructure",
                    "YaaJuu.Modules.Payments.Infrastructure")
                .GetResult();

            Assert.True(result.IsSuccessful, Format(result));
        }
    }

    [Fact]
    public void Shared_infrastructure_does_not_depend_on_api()
    {
        var result = Types.InAssembly(typeof(DependencyInjection).Assembly)
            .ShouldNot()
            .HaveDependencyOn("YaaJuu.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
    }

    [Fact]
    public void Identity_and_subscriptions_application_do_not_depend_on_tenancy()
    {
        var identity = Types.InAssembly(typeof(IdentityApplicationExtensions).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "YaaJuu.Modules.Tenancy.Application",
                "YaaJuu.Modules.Tenancy.Domain")
            .GetResult();

        var subscriptions = Types.InAssembly(typeof(SubscriptionsApplicationExtensions).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "YaaJuu.Modules.Tenancy.Application",
                "YaaJuu.Modules.Tenancy.Domain",
                "YaaJuu.Modules.Identity.Application")
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
                "YaaJuu.Modules.Tenancy.Application",
                "YaaJuu.Modules.Tenancy.Domain",
                "YaaJuu.Modules.Tenancy.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
    }

    [Fact]
    public void Pricing_application_does_not_depend_on_catalog_or_tenancy_modules()
    {
        var result = Types.InAssembly(typeof(PricingApplicationExtensions).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "YaaJuu.Modules.Catalog.Application",
                "YaaJuu.Modules.Catalog.Domain",
                "YaaJuu.Modules.Catalog.Infrastructure",
                "YaaJuu.Modules.Tenancy.Application",
                "YaaJuu.Modules.Tenancy.Domain",
                "YaaJuu.Modules.Tenancy.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
    }

    [Fact]
    public void Inventory_domain_does_not_depend_on_catalog_pricing_or_tenancy()
    {
        var result = Types.InAssembly(typeof(InventoryItem).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "YaaJuu.Api",
                "YaaJuu.Infrastructure",
                "YaaJuu.Modules.Catalog.Application",
                "YaaJuu.Modules.Catalog.Domain",
                "YaaJuu.Modules.Catalog.Infrastructure",
                "YaaJuu.Modules.Pricing.Application",
                "YaaJuu.Modules.Pricing.Domain",
                "YaaJuu.Modules.Pricing.Infrastructure",
                "YaaJuu.Modules.Tenancy.Application",
                "YaaJuu.Modules.Tenancy.Domain",
                "YaaJuu.Modules.Tenancy.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
    }

    [Fact]
    public void Inventory_application_does_not_depend_on_catalog_pricing_or_tenancy_modules()
    {
        var result = Types.InAssembly(typeof(InventoryApplicationExtensions).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "YaaJuu.Api",
                "YaaJuu.Infrastructure",
                "YaaJuu.Modules.Catalog.Application",
                "YaaJuu.Modules.Catalog.Domain",
                "YaaJuu.Modules.Catalog.Infrastructure",
                "YaaJuu.Modules.Pricing.Application",
                "YaaJuu.Modules.Pricing.Domain",
                "YaaJuu.Modules.Pricing.Infrastructure",
                "YaaJuu.Modules.Tenancy.Application",
                "YaaJuu.Modules.Tenancy.Domain",
                "YaaJuu.Modules.Tenancy.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
    }

    [Fact]
    public void Procurement_domain_does_not_depend_on_other_modules()
    {
        var result = Types.InAssembly(typeof(PurchaseOrder).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "YaaJuu.Api",
                "YaaJuu.Infrastructure",
                "YaaJuu.Modules.Catalog.Application",
                "YaaJuu.Modules.Catalog.Domain",
                "YaaJuu.Modules.Catalog.Infrastructure",
                "YaaJuu.Modules.Pricing.Application",
                "YaaJuu.Modules.Pricing.Domain",
                "YaaJuu.Modules.Pricing.Infrastructure",
                "YaaJuu.Modules.Inventory.Application",
                "YaaJuu.Modules.Inventory.Domain",
                "YaaJuu.Modules.Inventory.Infrastructure",
                "YaaJuu.Modules.Tenancy.Application",
                "YaaJuu.Modules.Tenancy.Domain",
                "YaaJuu.Modules.Tenancy.Infrastructure")
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
                "YaaJuu.Api",
                "YaaJuu.Infrastructure",
                "YaaJuu.Modules.Catalog.Application",
                "YaaJuu.Modules.Catalog.Domain",
                "YaaJuu.Modules.Catalog.Infrastructure",
                "YaaJuu.Modules.Pricing.Application",
                "YaaJuu.Modules.Pricing.Domain",
                "YaaJuu.Modules.Pricing.Infrastructure",
                "YaaJuu.Modules.Inventory.Application",
                "YaaJuu.Modules.Inventory.Domain",
                "YaaJuu.Modules.Inventory.Infrastructure",
                "YaaJuu.Modules.Tenancy.Application",
                "YaaJuu.Modules.Tenancy.Domain",
                "YaaJuu.Modules.Tenancy.Infrastructure")
            .GetResult();

        var usesContract = Types.InAssembly(typeof(ProcurementApplicationExtensions).Assembly)
            .That()
            .HaveNameEndingWith("ReceiveGoodsHandler")
            .Should()
            .HaveDependencyOn("YaaJuu.SharedKernel.Inventory")
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
                    "YaaJuu.Modules.Procurement.Application",
                    "YaaJuu.Modules.Procurement.Domain",
                    "YaaJuu.Modules.Procurement.Infrastructure",
                    "YaaJuu.Modules.Orders.Application",
                    "YaaJuu.Modules.Orders.Domain",
                    "YaaJuu.Modules.Orders.Infrastructure")
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
                    "YaaJuu.Api",
                    "YaaJuu.Infrastructure",
                    "YaaJuu.Modules.Catalog.Application",
                    "YaaJuu.Modules.Catalog.Domain",
                    "YaaJuu.Modules.Catalog.Infrastructure",
                    "YaaJuu.Modules.Pricing.Application",
                    "YaaJuu.Modules.Pricing.Domain",
                    "YaaJuu.Modules.Pricing.Infrastructure",
                    "YaaJuu.Modules.Inventory.Application",
                    "YaaJuu.Modules.Inventory.Domain",
                    "YaaJuu.Modules.Inventory.Infrastructure",
                    "YaaJuu.Modules.Procurement.Application",
                    "YaaJuu.Modules.Procurement.Domain",
                    "YaaJuu.Modules.Procurement.Infrastructure",
                    "YaaJuu.Modules.Tenancy.Application",
                    "YaaJuu.Modules.Tenancy.Domain",
                    "YaaJuu.Modules.Tenancy.Infrastructure",
                    "YaaJuu.Modules.Orders.Application",
                    "YaaJuu.Modules.Orders.Domain",
                    "YaaJuu.Modules.Orders.Infrastructure")
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
            typeof(ConsumerInfrastructureExtensions).Assembly,
            typeof(YaaJuu.Modules.Orders.Infrastructure.OrdersInfrastructureExtensions).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn("YaaJuu.Api")
                .GetResult();

            Assert.True(result.IsSuccessful, Format(result));
        }
    }

    [Fact]
    public void Orders_domain_does_not_depend_on_other_modules()
    {
        var result = Types.InAssembly(typeof(YaaJuu.Modules.Orders.Domain.Order).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "YaaJuu.Api",
                "YaaJuu.Infrastructure",
                "YaaJuu.Modules.Catalog.Application",
                "YaaJuu.Modules.Catalog.Domain",
                "YaaJuu.Modules.Catalog.Infrastructure",
                "YaaJuu.Modules.Pricing.Application",
                "YaaJuu.Modules.Pricing.Domain",
                "YaaJuu.Modules.Pricing.Infrastructure",
                "YaaJuu.Modules.Inventory.Application",
                "YaaJuu.Modules.Inventory.Domain",
                "YaaJuu.Modules.Inventory.Infrastructure",
                "YaaJuu.Modules.Consumer.Application",
                "YaaJuu.Modules.Consumer.Domain",
                "YaaJuu.Modules.Consumer.Infrastructure",
                "YaaJuu.Modules.Procurement.Application",
                "YaaJuu.Modules.Procurement.Domain",
                "YaaJuu.Modules.Procurement.Infrastructure",
                "YaaJuu.Modules.Tenancy.Application",
                "YaaJuu.Modules.Tenancy.Domain",
                "YaaJuu.Modules.Tenancy.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
    }

    /// <summary>
    /// Orders reserves stock through the SharedKernel reservation contract and resolves stores
    /// through SharedKernel.Discovery. It must not take a project reference to Inventory or Consumer.
    /// </summary>
    [Fact]
    public void Orders_application_reaches_inventory_only_through_shared_kernel()
    {
        var result = Types.InAssembly(typeof(YaaJuu.Modules.Orders.Application.OrdersApplicationExtensions).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "YaaJuu.Api",
                "YaaJuu.Infrastructure",
                "YaaJuu.Modules.Catalog.Application",
                "YaaJuu.Modules.Catalog.Domain",
                "YaaJuu.Modules.Catalog.Infrastructure",
                "YaaJuu.Modules.Pricing.Application",
                "YaaJuu.Modules.Pricing.Domain",
                "YaaJuu.Modules.Pricing.Infrastructure",
                "YaaJuu.Modules.Inventory.Application",
                "YaaJuu.Modules.Inventory.Domain",
                "YaaJuu.Modules.Inventory.Infrastructure",
                "YaaJuu.Modules.Consumer.Application",
                "YaaJuu.Modules.Consumer.Domain",
                "YaaJuu.Modules.Consumer.Infrastructure",
                "YaaJuu.Modules.Tenancy.Application",
                "YaaJuu.Modules.Tenancy.Domain",
                "YaaJuu.Modules.Tenancy.Infrastructure")
            .GetResult();

        var usesReservation = Types.InAssembly(typeof(YaaJuu.Modules.Orders.Application.OrdersApplicationExtensions).Assembly)
            .That()
            .HaveNameEndingWith("CreateOrderHandler")
            .Should()
            .HaveDependencyOn("YaaJuu.SharedKernel.Inventory")
            .GetResult();

        var usesDiscovery = Types.InAssembly(typeof(YaaJuu.Modules.Orders.Application.OrdersApplicationExtensions).Assembly)
            .That()
            .HaveNameEndingWith("CreateOrderHandler")
            .Should()
            .HaveDependencyOn("YaaJuu.SharedKernel.Discovery")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
        Assert.True(usesReservation.IsSuccessful, Format(usesReservation));
        Assert.True(usesDiscovery.IsSuccessful, Format(usesDiscovery));
    }

    /// <summary>
    /// Guards against a false green after a brand rename: module filters must inspect the real
    /// YaaJuu.Modules.* assemblies, not an empty set.
    /// </summary>
    [Fact]
    public void Architecture_tests_inspect_all_yaajuu_module_assemblies()
    {
        var names = new[]
        {
            typeof(RoleNames).Assembly.GetName().Name,
            typeof(IdentityApplicationExtensions).Assembly.GetName().Name,
            typeof(IdentityInfrastructureExtensions).Assembly.GetName().Name,
            typeof(Tenant).Assembly.GetName().Name,
            typeof(TenancyApplicationExtensions).Assembly.GetName().Name,
            typeof(TenancyInfrastructureExtensions).Assembly.GetName().Name,
            typeof(Plan).Assembly.GetName().Name,
            typeof(SubscriptionsApplicationExtensions).Assembly.GetName().Name,
            typeof(SubscriptionsInfrastructureExtensions).Assembly.GetName().Name,
            typeof(Category).Assembly.GetName().Name,
            typeof(CatalogApplicationExtensions).Assembly.GetName().Name,
            typeof(CatalogInfrastructureExtensions).Assembly.GetName().Name,
            typeof(GlobalProductPrice).Assembly.GetName().Name,
            typeof(PricingApplicationExtensions).Assembly.GetName().Name,
            typeof(PricingInfrastructureExtensions).Assembly.GetName().Name,
            typeof(InventoryItem).Assembly.GetName().Name,
            typeof(InventoryApplicationExtensions).Assembly.GetName().Name,
            typeof(InventoryInfrastructureExtensions).Assembly.GetName().Name,
            typeof(PurchaseOrder).Assembly.GetName().Name,
            typeof(ProcurementApplicationExtensions).Assembly.GetName().Name,
            typeof(ProcurementInfrastructureExtensions).Assembly.GetName().Name,
            typeof(StoreServiceArea).Assembly.GetName().Name,
            typeof(ConsumerApplicationExtensions).Assembly.GetName().Name,
            typeof(ConsumerInfrastructureExtensions).Assembly.GetName().Name,
            typeof(YaaJuu.Modules.Orders.Domain.Order).Assembly.GetName().Name,
            typeof(YaaJuu.Modules.Orders.Application.OrdersApplicationExtensions).Assembly.GetName().Name,
            typeof(YaaJuu.Modules.Orders.Infrastructure.OrdersInfrastructureExtensions).Assembly.GetName().Name,
            typeof(YaaJuu.Modules.Payments.Domain.Payment).Assembly.GetName().Name,
            typeof(YaaJuu.Modules.Payments.Application.PaymentsApplicationExtensions).Assembly.GetName().Name,
            typeof(YaaJuu.Modules.Payments.Infrastructure.PaymentsInfrastructureExtensions).Assembly.GetName().Name
        };

        Assert.Equal(30, names.Length);
        Assert.All(names, name =>
        {
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.StartsWith("YaaJuu.Modules.", name, StringComparison.Ordinal);
        });
        Assert.Equal(30, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Payments_domain_does_not_depend_on_orders_inventory_or_wompi_infrastructure()
    {
        var result = Types.InAssembly(typeof(YaaJuu.Modules.Payments.Domain.Payment).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "YaaJuu.Modules.Orders.Domain",
                "YaaJuu.Modules.Orders.Application",
                "YaaJuu.Modules.Orders.Infrastructure",
                "YaaJuu.Modules.Inventory.Domain",
                "YaaJuu.Modules.Inventory.Application",
                "YaaJuu.Modules.Inventory.Infrastructure",
                "YaaJuu.Modules.Consumer.Domain",
                "YaaJuu.Modules.Catalog.Domain",
                "YaaJuu.Modules.Pricing.Domain",
                "YaaJuu.Modules.Payments.Infrastructure",
                "YaaJuu.Modules.Payments.Application")
            .GetResult();
        Assert.True(result.IsSuccessful, Format(result));
    }

    [Fact]
    public void Payments_application_does_not_depend_on_concrete_wompi_adapter()
    {
        var result = Types.InAssembly(typeof(YaaJuu.Modules.Payments.Application.PaymentsApplicationExtensions).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "YaaJuu.Modules.Payments.Infrastructure",
                "YaaJuu.Modules.Orders.Domain",
                "YaaJuu.Modules.Inventory.Domain")
            .GetResult();
        Assert.True(result.IsSuccessful, Format(result));
    }

    [Fact]
    public void Orders_and_inventory_do_not_depend_on_payments_domain()
    {
        var orders = Types.InAssembly(typeof(YaaJuu.Modules.Orders.Domain.Order).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "YaaJuu.Modules.Payments.Domain",
                "YaaJuu.Modules.Payments.Application",
                "YaaJuu.Modules.Payments.Infrastructure")
            .GetResult();
        var inventory = Types.InAssembly(typeof(InventoryItem).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "YaaJuu.Modules.Payments.Domain",
                "YaaJuu.Modules.Payments.Application",
                "YaaJuu.Modules.Payments.Infrastructure")
            .GetResult();
        Assert.True(orders.IsSuccessful, Format(orders));
        Assert.True(inventory.IsSuccessful, Format(inventory));
    }

    private static string Format(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : string.Join(Environment.NewLine, result.FailingTypeNames ?? []);
}
