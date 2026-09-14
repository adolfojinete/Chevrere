using Chevrere.SharedKernel.Domain;

namespace Chevrere.Modules.Procurement.Domain;

/// <summary>
/// Vendor a tenant buys from. Belongs to one tenant; Code is unique per tenant.
/// </summary>
public sealed class Supplier : AggregateRoot
{
    private Supplier()
    {
        Code = null!;
        Name = null!;
    }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? TaxIdentification { get; private set; }

    public string? ContactName { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string? Address { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; }

    public static Supplier Create(
        Guid tenantId,
        string? code,
        string? name,
        SupplierContactDetails contact,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(contact);
        if (tenantId == Guid.Empty)
        {
            throw new DomainException("supplier.tenant.required", "A supplier must belong to a tenant.");
        }

        return new Supplier
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Code = NormalizeCode(code),
            Name = Guard.NotNullOrWhiteSpace(name, nameof(name), 200),
            TaxIdentification = Optional(contact.TaxIdentification, 50),
            ContactName = Optional(contact.ContactName, 160),
            Email = NormalizeEmail(contact.Email),
            Phone = Optional(contact.Phone, 40),
            Address = Optional(contact.Address, 500),
            Notes = Optional(contact.Notes, 1000),
            IsActive = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public void Update(string? name, SupplierContactDetails contact, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(contact);
        Name = Guard.NotNullOrWhiteSpace(name, nameof(name), 200);
        TaxIdentification = Optional(contact.TaxIdentification, 50);
        ContactName = Optional(contact.ContactName, 160);
        Email = NormalizeEmail(contact.Email);
        Phone = Optional(contact.Phone, 40);
        Address = Optional(contact.Address, 500);
        Notes = Optional(contact.Notes, 1000);
        UpdatedAt = utcNow;
    }

    /// <returns>true when IsActive actually changed to true.</returns>
    public bool Activate(DateTimeOffset utcNow)
    {
        if (IsActive)
        {
            return false;
        }

        IsActive = true;
        UpdatedAt = utcNow;
        return true;
    }

    /// <returns>true when IsActive actually changed to false.</returns>
    public bool Deactivate(DateTimeOffset utcNow)
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;
        UpdatedAt = utcNow;
        return true;
    }

    public void EnsureCanReceiveOrders()
    {
        if (!IsActive)
        {
            throw new DomainException(
                "supplier.not_active",
                "Only an active supplier can be used on a purchase order.");
        }
    }

    private static string NormalizeCode(string? code) =>
        Guard.NotNullOrWhiteSpace(code, nameof(code), 50).ToUpperInvariant();

    private static string? NormalizeEmail(string? email)
    {
        var normalized = Optional(email, 256);
        if (normalized is null)
        {
            return null;
        }

        if (!normalized.Contains('@', StringComparison.Ordinal))
        {
            throw new DomainException("supplier.email.invalid", "Supplier email is not a valid address.");
        }

        return normalized.ToLowerInvariant();
    }

    private static string? Optional(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Guard.NotNullOrWhiteSpace(value, nameof(value), maxLength);
}

/// <summary>
/// Optional supplier contact data, grouped to keep Create/Update signatures readable.
/// </summary>
public sealed record SupplierContactDetails(
    string? TaxIdentification = null,
    string? ContactName = null,
    string? Email = null,
    string? Phone = null,
    string? Address = null,
    string? Notes = null);
