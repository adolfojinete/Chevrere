using YaaJuu.SharedKernel.Domain;

namespace YaaJuu.Modules.Subscriptions.Domain.ValueObjects;

public sealed class Money : ValueObject
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Create(decimal amount, string? currency)
    {
        if (amount < 0)
        {
            throw new DomainException("money.negative", "Amount cannot be negative.");
        }

        var normalized = Guard.NotNullOrWhiteSpace(currency, nameof(currency), 3).ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsLetter))
        {
            throw new DomainException("money.currency.invalid", "Currency must be a 3-letter ISO code.");
        }

        return new Money(decimal.Round(amount, 2, MidpointRounding.AwayFromZero), normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}
