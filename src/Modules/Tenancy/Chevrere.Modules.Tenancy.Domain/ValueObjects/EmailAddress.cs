using System.Net.Mail;
using Chevrere.SharedKernel.Domain;

namespace Chevrere.Modules.Tenancy.Domain.ValueObjects;

public sealed class EmailAddress : ValueObject
{
    public const int MaxLength = 256;

    private EmailAddress(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static EmailAddress Create(string? value)
    {
        var trimmed = Guard.NotNullOrWhiteSpace(value, nameof(EmailAddress), MaxLength).ToLowerInvariant();

        try
        {
            _ = new MailAddress(trimmed);
        }
        catch (FormatException)
        {
            throw new DomainException("email.invalid", "The email address is not valid.");
        }

        return new EmailAddress(trimmed);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
