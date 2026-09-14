using System.Security.Claims;
using Chevrere.Api.Http;
using Chevrere.Api.Middleware;
using Chevrere.Infrastructure.Context;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Chevrere.UnitTests.Api;

public sealed class CorrelationAndHttpTests
{
    [Fact]
    public async Task Correlation_middleware_reuses_valid_header_or_generates_one()
    {
        var context = new DefaultHttpContext();
        var correlation = new CorrelationContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, correlation);
        Assert.True(Guid.TryParse(correlation.CorrelationId, out _));
        Assert.Equal(correlation.CorrelationId, context.Response.Headers[CorrelationIdMiddleware.HeaderName]);

        var known = Guid.CreateVersion7();
        var reused = new DefaultHttpContext();
        reused.Request.Headers[CorrelationIdMiddleware.HeaderName] = known.ToString("D");
        var second = new CorrelationContext();
        await middleware.InvokeAsync(reused, second);
        Assert.Equal(known.ToString("D"), second.CorrelationId);
    }

    [Fact]
    public void Correlation_context_requires_value()
    {
        var context = new CorrelationContext();
        Assert.Throws<InvalidOperationException>(() => _ = context.CorrelationId);
        Assert.Throws<ArgumentException>(() => context.Set(" "));
        context.Set("abc");
        Assert.Equal("abc", context.CorrelationId);
    }

    [Fact]
    public void System_clock_is_utc()
    {
        var clock = new SystemClock();
        Assert.Equal(TimeSpan.Zero, clock.UtcNow.Offset);
    }

    [Fact]
    public void Http_current_user_reads_claims()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()),
            new Claim(ClaimTypes.Email, "a@b.com"),
            new Claim(HttpCurrentUser.TenantIdClaim, Guid.CreateVersion7().ToString()),
            new Claim(ClaimTypes.Role, "PlatformAdmin")
        ], "test"));
        accessor.HttpContext.Returns(new DefaultHttpContext { User = user });

        var current = new HttpCurrentUser(accessor);
        Assert.True(current.IsAuthenticated);
        Assert.True(current.IsPlatformUser);
        Assert.Equal("a@b.com", current.Email);
        Assert.NotNull(current.UserId);
        Assert.NotNull(current.TenantId);
        Assert.Contains("PlatformAdmin", current.Roles);
    }

    [Fact]
    public void Result_extensions_map_status_codes()
    {
        var controller = new ProbeController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        Assert.IsType<NoContentResult>(controller.ToActionResult(Result.Success()));
        Assert.IsType<OkObjectResult>(controller.ToActionResult(Result.Success(1)));
        Assert.IsType<ObjectResult>(controller.ToActionResult(Result.Failure(Error.NotFound("n", "x"))));
        Assert.IsType<ObjectResult>(controller.ToProblem(Error.Validation("v", "x")));
        Assert.IsType<ObjectResult>(controller.ToProblem(Error.Conflict("c", "x")));
        Assert.IsType<ObjectResult>(controller.ToProblem(Error.Domain("d", "x")));
        Assert.IsType<ObjectResult>(controller.ToProblem(Error.Unauthorized("u", "x")));
        Assert.IsType<ObjectResult>(controller.ToProblem(Error.Forbidden("f", "x")));
        Assert.IsType<ObjectResult>(controller.FromConcurrency());

        var integrity = Assert.IsType<ObjectResult>(controller.ToProblem(Error.Failure("inventory.reservation.incomplete_set", "x")));
        Assert.Equal(StatusCodes.Status500InternalServerError, integrity.StatusCode);

        var created = controller.ToCreatedResult(Result.Success(1), "Get", new { id = 1 });
        Assert.IsType<CreatedAtActionResult>(created);
        Assert.IsType<ObjectResult>(controller.ToCreatedResult(Result.Failure<int>(Error.Conflict("c", "x")), "Get", new { id = 1 }));
    }

    private sealed class ProbeController : ControllerBase;
}
