using YaaJuu.Modules.Subscriptions.Domain;
using YaaJuu.Modules.Subscriptions.Domain.ValueObjects;
using YaaJuu.Modules.Tenancy.Domain;
using YaaJuu.Modules.Tenancy.Domain.ValueObjects;

namespace YaaJuu.UnitTests;

internal static class FixedClock
{
    public static DateTimeOffset Now { get; } = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
}

internal static class TestEntities
{
    public static Tenant Tenant(string code = "OP-NORTE") =>
        YaaJuu.Modules.Tenancy.Domain.Tenant.Create(TenantCode.Create(code), "Operador", FixedClock.Now);

    public static Franchisee Franchisee(Tenant? tenant = null, string code = "FR-NORTE")
    {
        tenant ??= Tenant();
        return YaaJuu.Modules.Tenancy.Domain.Franchisee.Create(
            tenant,
            TenantCode.Create(code),
            "Operador SAS",
            "YaaJuu Norte",
            Identification.Create(IdentificationType.Nit, "900123456"),
            EmailAddress.Create("ops@example.com"),
            PhoneNumber.Create("+573001112233"),
            FixedClock.Now);
    }

    public static Store Store(Tenant? tenant = null, Franchisee? franchisee = null)
    {
        tenant ??= Tenant();
        franchisee ??= Franchisee(tenant);
        return YaaJuu.Modules.Tenancy.Domain.Store.Create(
            tenant,
            franchisee,
            TenantCode.Create("DS-001"),
            "Dark Store Norte",
            "Calle 100 # 0-00",
            GeoCoordinate.Create(4.71m, -74.07m),
            FixedClock.Now);
    }

    public static Plan Plan() =>
        YaaJuu.Modules.Subscriptions.Domain.Plan.Create(
            "STANDARD",
            "YaaJuu Standard",
            "Seed",
            Money.Create(350000, "COP"),
            BillingPeriod.Monthly,
            FixedClock.Now);

    public static Subscription Subscription() =>
        YaaJuu.Modules.Subscriptions.Domain.Subscription.Start(
            Guid.CreateVersion7(),
            Plan(),
            SubscriptionStatus.Trial,
            14,
            FixedClock.Now);
}
