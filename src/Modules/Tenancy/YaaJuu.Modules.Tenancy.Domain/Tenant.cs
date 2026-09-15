using YaaJuu.SharedKernel.Domain;
using YaaJuu.Modules.Tenancy.Domain.ValueObjects;

namespace YaaJuu.Modules.Tenancy.Domain;

public sealed class Tenant : AggregateRoot
{
    private Tenant()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public TenantStatus Status { get; private set; }

    public static Tenant Create(TenantCode code, string name, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(code);

        return new Tenant
        {
            Id = Guid.CreateVersion7(),
            Code = code.Value,
            Name = Guard.NotNullOrWhiteSpace(name, nameof(name), 200),
            Status = TenantStatus.Pending,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public void Activate(DateTimeOffset utcNow)
    {
        if (Status is TenantStatus.Inactive)
        {
            throw new DomainException("tenant.inactive", "An inactive tenant cannot be activated.");
        }

        Status = TenantStatus.Active;
        UpdatedAt = utcNow;
    }

    public void Suspend(DateTimeOffset utcNow)
    {
        if (Status is TenantStatus.Inactive)
        {
            throw new DomainException("tenant.inactive", "An inactive tenant cannot be suspended.");
        }

        Status = TenantStatus.Suspended;
        UpdatedAt = utcNow;
    }
}
