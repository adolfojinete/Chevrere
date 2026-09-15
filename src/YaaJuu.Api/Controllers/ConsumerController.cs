using System.Diagnostics.CodeAnalysis;
using YaaJuu.Api.Http;
using YaaJuu.Modules.Consumer.Application.Contracts;
using YaaJuu.Modules.Consumer.Application.Queries;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YaaJuu.Api.Controllers;

/// <summary>
/// Consumer surface. Anonymous by design: a shopper checks coverage and browses before signing up.
/// Coordinates arrive in the body, never in the URL, so they do not end up in access logs.
/// </summary>
[ApiController]
[Route("api/v1/consumer")]
[AllowAnonymous]
[SuppressMessage("csharpsquid", "S6960", Justification = "REST resource: the consumer discovery surface stays together.")]
public sealed class ConsumerController(
    IHandler<CheckCoverageQuery, Result<CoverageDto>> coverageHandler,
    IHandler<SearchConsumerCatalogQuery, Result<ConsumerCatalogResponse>> catalogHandler,
    IHandler<SearchConsumerCategoriesQuery, Result<ConsumerCategoriesResponse>> categoriesHandler,
    IHandler<GetConsumerProductDetailQuery, Result<ConsumerProductDetailDto>> productHandler,
    IValidator<ConsumerLocationRequest> locationValidator,
    IValidator<ConsumerCatalogSearchRequest> searchValidator) : ControllerBase
{
    [HttpPost("coverage")]
    [ProducesResponseType(typeof(CoverageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Coverage(
        [FromBody] ConsumerLocationRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await locationValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        return this.ToActionResult(await coverageHandler.HandleAsync(
            new CheckCoverageQuery(request.Latitude, request.Longitude),
            cancellationToken));
    }

    [HttpPost("catalog/search")]
    [ProducesResponseType(typeof(ConsumerCatalogResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchCatalog(
        [FromBody] ConsumerCatalogSearchRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await searchValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        return this.ToActionResult(await catalogHandler.HandleAsync(
            new SearchConsumerCatalogQuery(request),
            cancellationToken));
    }

    [HttpPost("categories")]
    [ProducesResponseType(typeof(ConsumerCategoriesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Categories(
        [FromBody] ConsumerLocationRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await locationValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        return this.ToActionResult(await categoriesHandler.HandleAsync(
            new SearchConsumerCategoriesQuery(request.Latitude, request.Longitude),
            cancellationToken));
    }

    /// <summary>
    /// POST, not GET: the request carries the consumer's coordinates. Outside coverage the answer is
    /// 404, the same as for a product that is not commercially visible here.
    /// </summary>
    [HttpPost("products/{productId:guid}")]
    [ProducesResponseType(typeof(ConsumerProductDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Product(
        Guid productId,
        [FromBody] ConsumerLocationRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await locationValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        return this.ToActionResult(await productHandler.HandleAsync(
            new GetConsumerProductDetailQuery(productId, request.Latitude, request.Longitude),
            cancellationToken));
    }
}
