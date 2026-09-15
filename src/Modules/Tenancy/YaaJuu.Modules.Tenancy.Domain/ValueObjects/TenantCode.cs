using System.Text.RegularExpressions;
using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.Modules.Tenancy.Domain.ValueObjects;

public sealed partial class TenantCode : ValueObject
{
    public const int MinLength = 3;
    public const int MaxLength = 50;

    private TenantCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static TenantCode Create(string? value)
    {
        var normalized = Guard.NotNullOrWhiteSpace(value, nameof(TenantCode), MaxLength).ToUpperInvariant();

        if (normalized.Length < MinLength)
        {
            throw new DomainException("tenant.code.too_short", $"Tenant code must have at least {MinLength} characters.");
        }

        if (!CodePattern().IsMatch(normalized))
        {
            throw new DomainException(
                "tenant.code.invalid",
                "Tenant code may contain only letters, numbers and hyphens.");
        }

        return new TenantCode(normalized);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    [GeneratedRegex("^[A-Z0-9]+(?:-[A-Z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
