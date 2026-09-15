using YaaJuu.Modules.Pricing.Domain;
using YaaJuu.SharedKernel.Domain.ValueObjects;

namespace YaaJuu.Modules.Pricing.Application.Contracts;

public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record GlobalPriceDto(
    Guid GlobalProductId,
    MoneyDto? SuggestedPrice,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? UpdatedAt);

public sealed record PriceHistoryItemDto(
    Guid Id,
    MoneyDto Price,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo);

public sealed record SetPriceRequest(decimal Amount, string Currency);

public sealed record StoreEffectivePriceDto(
    Guid StoreId,
    Guid GlobalProductId,
    MoneyDto? SuggestedPrice,
    MoneyDto? OverridePrice,
    MoneyDto? EffectivePrice,
    string? Currency,
    PriceSource Source);

public sealed record StorePriceListItemDto(
    Guid StoreId,
    Guid GlobalProductId,
    string Sku,
    string Name,
    MoneyDto? SuggestedPrice,
    MoneyDto? OverridePrice,
    MoneyDto? EffectivePrice,
    string? Currency,
    PriceSource Source);

public static class MoneyMapping
{
    public static MoneyDto ToDto(this Money money) => new(money.Amount, money.Currency);

    public static MoneyDto? ToNullableDto(this Money? money) => money is null ? null : money.ToDto();
}
