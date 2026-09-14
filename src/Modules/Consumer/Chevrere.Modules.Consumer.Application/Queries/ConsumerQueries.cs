using Chevrere.Modules.Consumer.Application.Abstractions;
using Chevrere.Modules.Consumer.Application.Contracts;
using Chevrere.Modules.Consumer.Domain.ValueObjects;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Discovery;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Results;

namespace Chevrere.Modules.Consumer.Application.Queries;

public sealed record CheckCoverageQuery(double Latitude, double Longitude);

public sealed class CheckCoverageHandler(IConsumerStoreResolver resolver)
    : IHandler<CheckCoverageQuery, Result<CoverageDto>>
{
    public async Task<Result<CoverageDto>> HandleAsync(
        CheckCoverageQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ConsumerLocation.TryCreate(request.Latitude, request.Longitude, out var location, out var error))
        {
            return Result.Failure<CoverageDto>(error);
        }

        var store = await resolver.ResolveEligibleStoreAsync(
            location.Latitude, location.Longitude, cancellationToken);
        return Result.Success(new CoverageDto(store is not null));
    }
}

public sealed record SearchConsumerCatalogQuery(ConsumerCatalogSearchRequest Request);

public sealed class SearchConsumerCatalogHandler(
    IConsumerStoreResolver resolver,
    IConsumerCatalogReadStore catalog)
    : IHandler<SearchConsumerCatalogQuery, Result<ConsumerCatalogResponse>>
{
    public async Task<Result<ConsumerCatalogResponse>> HandleAsync(
        SearchConsumerCatalogQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = Math.Max(1, request.Request.Page);
        var pageSize = Math.Clamp(request.Request.PageSize, 1, 100);

        if (!ConsumerLocation.TryCreate(
                request.Request.Latitude,
                request.Request.Longitude,
                out var location,
                out var error))
        {
            return Result.Failure<ConsumerCatalogResponse>(error);
        }

        var store = await resolver.ResolveEligibleStoreAsync(
            location.Latitude, location.Longitude, cancellationToken);
        if (store is null)
        {
            return Result.Success(new ConsumerCatalogResponse(false, page, pageSize, 0, []));
        }

        var (items, total) = await catalog.SearchProductsAsync(
            store,
            request.Request.Search,
            request.Request.CategoryId,
            page,
            pageSize,
            cancellationToken);

        return Result.Success(new ConsumerCatalogResponse(true, page, pageSize, total, items));
    }
}

public sealed record SearchConsumerCategoriesQuery(double Latitude, double Longitude);

public sealed class SearchConsumerCategoriesHandler(
    IConsumerStoreResolver resolver,
    IConsumerCatalogReadStore catalog)
    : IHandler<SearchConsumerCategoriesQuery, Result<ConsumerCategoriesResponse>>
{
    public async Task<Result<ConsumerCategoriesResponse>> HandleAsync(
        SearchConsumerCategoriesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ConsumerLocation.TryCreate(request.Latitude, request.Longitude, out var location, out var error))
        {
            return Result.Failure<ConsumerCategoriesResponse>(error);
        }

        var store = await resolver.ResolveEligibleStoreAsync(
            location.Latitude, location.Longitude, cancellationToken);
        if (store is null)
        {
            return Result.Success(new ConsumerCategoriesResponse(false, []));
        }

        var categories = await catalog.ListCategoriesAsync(store, cancellationToken);
        return Result.Success(new ConsumerCategoriesResponse(true, categories));
    }
}

public sealed record GetConsumerProductDetailQuery(Guid ProductId, double Latitude, double Longitude);

/// <summary>
/// Without coverage the product does not exist for this consumer. Returning 200 with a null body, or
/// a distinct "no coverage" code, would let anybody probe the network's footprint product by product.
/// </summary>
public sealed class GetConsumerProductDetailHandler(
    IConsumerStoreResolver resolver,
    IConsumerCatalogReadStore catalog)
    : IHandler<GetConsumerProductDetailQuery, Result<ConsumerProductDetailDto>>
{
    public async Task<Result<ConsumerProductDetailDto>> HandleAsync(
        GetConsumerProductDetailQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ConsumerLocation.TryCreate(request.Latitude, request.Longitude, out var location, out var error))
        {
            return Result.Failure<ConsumerProductDetailDto>(error);
        }

        var store = await resolver.ResolveEligibleStoreAsync(
            location.Latitude, location.Longitude, cancellationToken);
        if (store is null)
        {
            return Result.Failure<ConsumerProductDetailDto>(NotFound());
        }

        var product = await catalog.GetProductAsync(store, request.ProductId, cancellationToken);
        return product is null
            ? Result.Failure<ConsumerProductDetailDto>(NotFound())
            : Result.Success(product);
    }

    private static Error NotFound() => Error.NotFound(ErrorCodes.NotFound, "Product not found.");
}

internal static class ConsumerLocation
{
    public static bool TryCreate(
        double latitude,
        double longitude,
        out GeoCoordinate location,
        out Error error)
    {
        try
        {
            location = GeoCoordinate.Create(latitude, longitude);
            error = null!;
            return true;
        }
        catch (DomainException ex)
        {
            location = null!;
            error = Error.Validation(ex.Code, ex.Message);
            return false;
        }
    }
}
