using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.Modules.Tenancy.Domain.ValueObjects;

public sealed class Identification : ValueObject
{
    public const int MaxLength = 32;

    private Identification(IdentificationType type, string number)
    {
        Type = type;
        Number = number;
    }

    public IdentificationType Type { get; }

    public string Number { get; }

    public static Identification Create(IdentificationType type, string? number)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainException("identification.type.invalid", "Identification type is not supported.");
        }

        var normalized = Guard.NotNullOrWhiteSpace(number, nameof(Identification), MaxLength)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        if (normalized.Length < 5)
        {
            throw new DomainException("identification.number.invalid", "Identification number is too short.");
        }

        return new Identification(type, normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Type;
        yield return Number;
    }
}
