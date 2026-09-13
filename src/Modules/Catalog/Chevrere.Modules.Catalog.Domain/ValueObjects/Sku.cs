using System.Text.RegularExpressions;
using Chevrere.SharedKernel.Domain;

namespace Chevrere.Modules.Catalog.Domain.ValueObjects;

public sealed partial class Sku : ValueObject
{
    public const int MinLength = 3;
    public const int MaxLength = 50;

    private Sku(string value) => Value = value;

    public string Value { get; }

    public static Sku Create(string? value)
    {
        var normalized = Guard.NotNullOrWhiteSpace(value, nameof(Sku), MaxLength).ToUpperInvariant();
        if (normalized.Length < MinLength)
        {
            throw new DomainException("product.sku.too_short", $"SKU must have at least {MinLength} characters.");
        }

        if (!SkuPattern().IsMatch(normalized))
        {
            throw new DomainException(
                "product.sku.invalid",
                "SKU may contain only letters, numbers and hyphens.");
        }

        return new Sku(normalized);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    [GeneratedRegex("^[A-Z0-9]+(?:-[A-Z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SkuPattern();
}
