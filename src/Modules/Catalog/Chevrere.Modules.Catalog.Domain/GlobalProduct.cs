using Chevrere.Modules.Catalog.Domain.ValueObjects;
using Chevrere.SharedKernel.Domain;

namespace Chevrere.Modules.Catalog.Domain;

public sealed class GlobalProduct : AggregateRoot
{
    private GlobalProduct()
    {
        Sku = null!;
        Name = null!;
        Slug = null!;
        Brand = null!;
        Presentation = null!;
    }

    public Guid CategoryId { get; private set; }

    public string Sku { get; private set; }

    public string Name { get; private set; }

    public string Slug { get; private set; }

    public string? Description { get; private set; }

    public string Brand { get; private set; }

    public string Presentation { get; private set; }

    public string? Barcode { get; private set; }

    public GlobalProductStatus Status { get; private set; }

    public bool IsActive => Status == GlobalProductStatus.Active;

    public static GlobalProduct Create(
        Category category,
        Sku sku,
        string name,
        Slug slug,
        string brand,
        string presentation,
        string? description,
        Barcode? barcode,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(slug);

        return new GlobalProduct
        {
            Id = Guid.CreateVersion7(),
            CategoryId = category.Id,
            Sku = sku.Value,
            Name = Guard.NotNullOrWhiteSpace(name, nameof(name), 160),
            Slug = slug.Value,
            Brand = Guard.NotNullOrWhiteSpace(brand, nameof(brand), 80),
            Presentation = Guard.NotNullOrWhiteSpace(presentation, nameof(presentation), 80),
            Description = NormalizeOptional(description, 500),
            Barcode = barcode?.Value,
            Status = GlobalProductStatus.Active,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public void Update(
        Category category,
        string name,
        string brand,
        string presentation,
        string? description,
        Barcode? barcode,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(category);
        CategoryId = category.Id;
        Name = Guard.NotNullOrWhiteSpace(name, nameof(name), 160);
        Brand = Guard.NotNullOrWhiteSpace(brand, nameof(brand), 80);
        Presentation = Guard.NotNullOrWhiteSpace(presentation, nameof(presentation), 80);
        Description = NormalizeOptional(description, 500);
        Barcode = barcode?.Value;
        UpdatedAt = utcNow;
    }

    public void Activate(DateTimeOffset utcNow)
    {
        if (Status == GlobalProductStatus.Discontinued)
        {
            throw new DomainException(
                "product.discontinued",
                "A discontinued product cannot be activated.");
        }

        Status = GlobalProductStatus.Active;
        UpdatedAt = utcNow;
    }

    public void Deactivate(DateTimeOffset utcNow)
    {
        if (Status == GlobalProductStatus.Discontinued)
        {
            throw new DomainException(
                "product.discontinued",
                "A discontinued product cannot be deactivated.");
        }

        Status = GlobalProductStatus.Inactive;
        UpdatedAt = utcNow;
    }

    public void EnsureCanBeOffered()
    {
        if (!IsActive)
        {
            throw new DomainException(
                "product.not_active",
                "Only an active global product can be enabled for a store.");
        }
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Guard.NotNullOrWhiteSpace(value, nameof(Description), maxLength);
    }
}
