using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.Modules.Catalog.Domain.ValueObjects;

public sealed partial class Slug : ValueObject
{
    public const int MaxLength = 80;

    private Slug(string value) => Value = value;

    public string Value { get; }

    public static Slug Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("slug.required", "Slug is required.");
        }

        var slug = Normalize(value);
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new DomainException("slug.invalid", "Slug must contain at least one letter or number.");
        }

        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
        }

        return new Slug(slug);
    }

    public static Slug FromName(string name) => Create(name);

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    private static string Normalize(string value)
    {
        var formD = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(formD.Length);
        foreach (var character in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        var ascii = builder.ToString().Normalize(NormalizationForm.FormC);
        ascii = SlugCleanup().Replace(ascii, "-");
        return ascii.Trim('-');
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex SlugCleanup();
}
