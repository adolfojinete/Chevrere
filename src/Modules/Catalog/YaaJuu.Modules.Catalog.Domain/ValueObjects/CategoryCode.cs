using System.Text.RegularExpressions;
using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.Modules.Catalog.Domain.ValueObjects;

public sealed partial class CategoryCode : ValueObject
{
    public const int MinLength = 2;
    public const int MaxLength = 50;

    private CategoryCode(string value) => Value = value;

    public string Value { get; }

    public static CategoryCode Create(string? value)
    {
        var normalized = Guard.NotNullOrWhiteSpace(value, nameof(CategoryCode), MaxLength).ToUpperInvariant();
        if (normalized.Length < MinLength)
        {
            throw new DomainException("category.code.too_short", $"Category code must have at least {MinLength} characters.");
        }

        if (!CodePattern().IsMatch(normalized))
        {
            throw new DomainException(
                "category.code.invalid",
                "Category code may contain only letters, numbers and hyphens.");
        }

        return new CategoryCode(normalized);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    [GeneratedRegex("^[A-Z0-9]+(?:-[A-Z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
