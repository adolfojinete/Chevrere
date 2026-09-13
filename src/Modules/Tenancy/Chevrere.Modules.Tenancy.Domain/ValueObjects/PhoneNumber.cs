using Chevrere.SharedKernel.Domain;

namespace Chevrere.Modules.Tenancy.Domain.ValueObjects;

public sealed class PhoneNumber : ValueObject
{
    public const int MaxLength = 32;

    private PhoneNumber(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static PhoneNumber Create(string? value)
    {
        var trimmed = Guard.NotNullOrWhiteSpace(value, nameof(PhoneNumber), MaxLength);

        if (trimmed.Length < 7)
        {
            throw new DomainException("phone.invalid", "Phone number is too short.");
        }

        return new PhoneNumber(trimmed);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
