using Chevrere.Api.Http;
using Chevrere.Infrastructure.Persistence;
using Chevrere.SharedKernel.Persistence;
using Chevrere.Modules.Identity.Application.Contracts;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chevrere.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    IHandler<LoginRequest, Result<LoginResponse>> handler,
    IValidator<LoginRequest> validator) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return this.ToValidationProblem(validation);
        }

        try
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return this.ToActionResult(result);
        }
        catch (ConcurrencyConflictException)
        {
            return this.FromConcurrency();
        }
    }
}
