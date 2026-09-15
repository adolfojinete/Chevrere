using System.Text.RegularExpressions;
using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.Modules.Catalog.Domain.ValueObjects;

public sealed partial class Barcode : ValueObject
{
    public const int MaxLength = 64;

    private Barcode(string value) => Value = value;

    public string Value { get; }

    public static Barcode? Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > MaxLength)
        {
            throw new DomainException("product.barcode.max_length", $"Barcode cannot exceed {MaxLength} characters.");
        }

        if (!BarcodePattern().IsMatch(normalized))
        {
            throw new DomainException("product.barcode.invalid", "Barcode may contain only digits.");
        }

        return new Barcode(normalized);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    [GeneratedRegex("^[0-9]{8,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex BarcodePattern();
}
