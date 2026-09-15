using YaaJuu.SharedKernel.Domain;
using YaaJuu.Modules.Tenancy.Domain.ValueObjects;

namespace YaaJuu.Modules.Tenancy.Domain;

public sealed class Franchisee : AggregateRoot
{
    private Franchisee()
    {
        Code = null!;
        LegalName = null!;
        TradeName = null!;
        IdentificationNumber = null!;
        Email = null!;
        Phone = null!;
    }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; }

    public string LegalName { get; private set; }

    public string TradeName { get; private set; }

    public IdentificationType IdentificationType { get; private set; }

    public string IdentificationNumber { get; private set; }

    public string Email { get; private set; }

    public string Phone { get; private set; }

    public FranchiseeStatus Status { get; private set; }

    public static Franchisee Create(
        Tenant tenant,
        TenantCode code,
        string legalName,
        string tradeName,
        Identification identification,
        EmailAddress email,
        PhoneNumber phone,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(identification);
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(phone);

        return new Franchisee
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Code = code.Value,
            LegalName = Guard.NotNullOrWhiteSpace(legalName, nameof(legalName), 200),
            TradeName = Guard.NotNullOrWhiteSpace(tradeName, nameof(tradeName), 200),
            IdentificationType = identification.Type,
            IdentificationNumber = identification.Number,
            Email = email.Value,
            Phone = phone.Value,
            Status = FranchiseeStatus.Pending,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public void Activate(DateTimeOffset utcNow)
    {
        EnsureTransition(FranchiseeStatus.Pending, FranchiseeStatus.Active);
        Status = FranchiseeStatus.Active;
        UpdatedAt = utcNow;
    }

    public void Suspend(DateTimeOffset utcNow)
    {
        EnsureTransition(FranchiseeStatus.Active, FranchiseeStatus.Suspended);
        Status = FranchiseeStatus.Suspended;
        UpdatedAt = utcNow;
    }

    public void Reactivate(DateTimeOffset utcNow)
    {
        EnsureTransition(FranchiseeStatus.Suspended, FranchiseeStatus.Active);
        Status = FranchiseeStatus.Active;
        UpdatedAt = utcNow;
    }

    public void Reject(DateTimeOffset utcNow)
    {
        EnsureTransition(FranchiseeStatus.Pending, FranchiseeStatus.Rejected);
        Status = FranchiseeStatus.Rejected;
        UpdatedAt = utcNow;
    }

    public void Deactivate(DateTimeOffset utcNow)
    {
        if (Status is FranchiseeStatus.Rejected)
        {
            throw new DomainException("franchisee.invalid_transition", "A rejected franchisee cannot be deactivated.");
        }

        Status = FranchiseeStatus.Inactive;
        UpdatedAt = utcNow;
    }

    public bool BelongsTo(Guid tenantId) => TenantId == tenantId;

    private void EnsureTransition(FranchiseeStatus expected, FranchiseeStatus target)
    {
        if (Status != expected)
        {
            throw new DomainException(
                "franchisee.invalid_transition",
                $"Cannot change franchisee from {Status} to {target}.");
        }
    }
}
