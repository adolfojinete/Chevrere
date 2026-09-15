using YaaJuu.SharedKernel.Domain;
using YaaJuu.Modules.Subscriptions.Domain.ValueObjects;

namespace YaaJuu.Modules.Subscriptions.Domain;

public sealed class Plan : AggregateRoot
{
    private Plan()
    {
        Code = null!;
        Name = null!;
        Currency = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public decimal MonthlyPrice { get; private set; }

    public string Currency { get; private set; }

    public BillingPeriod BillingPeriod { get; private set; }

    public bool IsActive { get; private set; }

    public static Plan Create(
        string code,
        string name,
        string? description,
        Money price,
        BillingPeriod billingPeriod,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(price);

        return new Plan
        {
            Id = Guid.CreateVersion7(),
            Code = Guard.NotNullOrWhiteSpace(code, nameof(code), 50).ToUpperInvariant(),
            Name = Guard.NotNullOrWhiteSpace(name, nameof(name), 120),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            MonthlyPrice = price.Amount,
            Currency = price.Currency,
            BillingPeriod = billingPeriod,
            IsActive = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public void Rename(string name, DateTimeOffset utcNow)
    {
        var next = Guard.NotNullOrWhiteSpace(name, nameof(name), 120);
        if (Name == next)
        {
            return;
        }

        Name = next;
        UpdatedAt = utcNow;
    }

    public void Deactivate(DateTimeOffset utcNow)
    {
        IsActive = false;
        UpdatedAt = utcNow;
    }
}
