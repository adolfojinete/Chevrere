using Chevrere.Modules.Catalog.Domain.ValueObjects;
using Chevrere.SharedKernel.Domain;

namespace Chevrere.Modules.Catalog.Domain;

public sealed class Category : AggregateRoot
{
    private Category()
    {
        Code = null!;
        Name = null!;
        Slug = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string Slug { get; private set; }

    public string? Description { get; private set; }

    public CategoryStatus Status { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsActive => Status == CategoryStatus.Active;

    public static Category Create(
        CategoryCode code,
        string name,
        Slug slug,
        string? description,
        int sortOrder,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(slug);

        return new Category
        {
            Id = Guid.CreateVersion7(),
            Code = code.Value,
            Name = Guard.NotNullOrWhiteSpace(name, nameof(name), 80),
            Slug = slug.Value,
            Description = NormalizeOptional(description, 300),
            Status = CategoryStatus.Active,
            SortOrder = sortOrder < 0 ? 0 : sortOrder,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public void Update(string name, string? description, int sortOrder, DateTimeOffset utcNow)
    {
        Name = Guard.NotNullOrWhiteSpace(name, nameof(name), 80);
        Description = NormalizeOptional(description, 300);
        SortOrder = sortOrder < 0 ? 0 : sortOrder;
        UpdatedAt = utcNow;
    }

    /// <returns>true when the status actually changed.</returns>
    public bool Activate(DateTimeOffset utcNow)
    {
        if (Status == CategoryStatus.Active)
        {
            return false;
        }

        Status = CategoryStatus.Active;
        UpdatedAt = utcNow;
        return true;
    }

    /// <returns>true when the status actually changed.</returns>
    public bool Deactivate(DateTimeOffset utcNow)
    {
        if (Status == CategoryStatus.Inactive)
        {
            return false;
        }

        Status = CategoryStatus.Inactive;
        UpdatedAt = utcNow;
        return true;
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
