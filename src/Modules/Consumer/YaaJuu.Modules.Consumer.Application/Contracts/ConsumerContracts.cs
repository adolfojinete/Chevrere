namespace YaaJuu.Modules.Consumer.Application.Contracts;

public sealed record ConsumerLocationRequest(double Latitude, double Longitude);

public sealed class ConsumerCatalogSearchRequest
{
    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public string? Search { get; init; }

    public Guid? CategoryId { get; init; }
}

/// <summary>
/// The only thing a consumer learns about the network: whether somebody delivers here. Never which
/// store, never how far.
/// </summary>
public sealed record CoverageDto(bool ServiceAvailable);

public sealed record ConsumerCatalogResponse(
    bool ServiceAvailable,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<ConsumerProductListItemDto> Items);

public sealed record ConsumerCategoriesResponse(
    bool ServiceAvailable,
    IReadOnlyList<ConsumerCategoryDto> Items);

/// <summary>
/// Id is the GlobalProductId. No StoreProduct id, no tenant, no stock figure: the consumer buys from
/// YaaJuu, not from a dark store they are allowed to identify.
/// </summary>
public sealed record ConsumerProductListItemDto(
    Guid Id,
    string Sku,
    string Name,
    string Brand,
    string Presentation,
    string? Description,
    Guid CategoryId,
    string CategoryName,
    decimal Price,
    string Currency);

public sealed record ConsumerProductDetailDto(
    Guid Id,
    string Sku,
    string Name,
    string Brand,
    string Presentation,
    string? Description,
    Guid CategoryId,
    string CategoryName,
    decimal Price,
    string Currency);

public sealed record ConsumerCategoryDto(Guid Id, string Name, string? Slug);

public sealed record ConfigureServiceAreaRequest(
    double Latitude,
    double Longitude,
    int ServiceRadiusMeters);

/// <summary>
/// Back-office view. Unlike the consumer DTOs this one may expose the geometry: the operator drew it.
/// </summary>
public sealed record AdminServiceAreaDto(
    Guid Id,
    Guid StoreId,
    Guid TenantId,
    double Latitude,
    double Longitude,
    int ServiceRadiusMeters,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
