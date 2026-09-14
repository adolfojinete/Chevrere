using Chevrere.Modules.Pricing.Domain.ValueObjects;
using Chevrere.SharedKernel.Domain;

namespace Chevrere.Modules.Pricing.Domain;

/// <summary>
/// Versioned suggested price for a global product. Current row has ValidTo = null.
/// </summary>
public sealed class GlobalProductPrice : AggregateRoot
{
    private GlobalProductPrice()
    {
        Currency = null!;
    }

    public Guid GlobalProductId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public DateTimeOffset ValidFrom { get; private set; }

    public DateTimeOffset? ValidTo { get; private set; }

    public bool IsCurrent => ValidTo is null;

    public Money AsMoney() => Money.FromPersistence(Amount, Currency);

    public static GlobalProductPrice Open(Guid globalProductId, Money money, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(money);
        if (globalProductId == Guid.Empty)
        {
            throw new DomainException("price.product.required", "Global product is required.");
        }

        return new GlobalProductPrice
        {
            Id = Guid.CreateVersion7(),
            GlobalProductId = globalProductId,
            Amount = money.Amount,
            Currency = money.Currency,
            ValidFrom = utcNow,
            ValidTo = null,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public void Close(DateTimeOffset utcNow)
    {
        if (ValidTo is not null)
        {
            throw new DomainException("price.already_closed", "This price version is already closed.");
        }

        ValidTo = utcNow;
        UpdatedAt = utcNow;
    }
}
