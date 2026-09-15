using YaaJuu.SharedKernel.Domain;
using YaaJuu.Modules.Tenancy.Domain.ValueObjects;

namespace YaaJuu.Modules.Tenancy.Domain;

public sealed class Store : AggregateRoot
{
    private Store()
    {
        Code = null!;
        Name = null!;
        AddressInternal = null!;
    }

    public Guid TenantId { get; private set; }

    public Guid FranchiseeId { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public StoreStatus Status { get; private set; }

    public string AddressInternal { get; private set; }

    public decimal? Latitude { get; private set; }

    public decimal? Longitude { get; private set; }

    public static Store Create(
        Tenant tenant,
        Franchisee franchisee,
        TenantCode code,
        string name,
        string addressInternal,
        GeoCoordinate? location,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(franchisee);
        ArgumentNullException.ThrowIfNull(code);

        if (franchisee.TenantId != tenant.Id)
        {
            throw new DomainException(
                "store.tenant_mismatch",
                "A store must belong to the same tenant as its franchisee.");
        }

        return new Store
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            FranchiseeId = franchisee.Id,
            Code = code.Value,
            Name = Guard.NotNullOrWhiteSpace(name, nameof(name), 200),
            AddressInternal = Guard.NotNullOrWhiteSpace(addressInternal, nameof(addressInternal), 500),
            Latitude = location?.Latitude,
            Longitude = location?.Longitude,
            Status = StoreStatus.Pending,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public void Activate(DateTimeOffset utcNow)
    {
        Status = StoreStatus.Active;
        UpdatedAt = utcNow;
    }

    public void Suspend(DateTimeOffset utcNow)
    {
        Status = StoreStatus.Suspended;
        UpdatedAt = utcNow;
    }

    public bool BelongsTo(Guid tenantId, Guid franchiseeId) =>
        TenantId == tenantId && FranchiseeId == franchiseeId;
}
