namespace Chevrere.SharedKernel.Domain.ValueObjects;

/// <summary>
/// Supported ISO-4217 currency codes for Chevrere. Expand here as markets grow.
/// </summary>
public static class CurrencyCodes
{
    public const string Cop = "COP";

    public static bool IsSupported(string currency) =>
        string.Equals(currency, Cop, StringComparison.Ordinal);
}

/// <summary>
/// Monetary amount shared by every module that handles money (prices, costs).
/// Always positive, single supported currency, fixed scale of 2.
/// </summary>
public sealed class Money : ValueObject
{
    public const int Scale = 2;

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Create(decimal amount, string? currency)
    {
        if (amount <= 0m)
        {
            throw new DomainException("money.amount.invalid", "Amount must be greater than zero.");
        }

        var code = Guard.NotNullOrWhiteSpace(currency, nameof(currency), 3).ToUpperInvariant();
        if (code.Length != 3 || !CurrencyCodes.IsSupported(code))
        {
            throw new DomainException("money.currency.unsupported", $"Currency '{code}' is not supported.");
        }

        var normalized = decimal.Round(amount, Scale, MidpointRounding.AwayFromZero);
        if (normalized != amount)
        {
            throw new DomainException(
                "money.amount.scale",
                $"Amount cannot have more than {Scale} decimal places.");
        }

        return new Money(normalized, code);
    }

    public static Money FromPersistence(decimal amount, string currency) =>
        new(amount, currency);

    public override string ToString() => $"{Amount} {Currency}";

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}
