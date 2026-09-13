using Chevrere.SharedKernel.Context;
using Serilog.Context;

namespace Chevrere.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlation)
    {
        var incoming = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = Guid.TryParse(incoming, out var parsed)
            ? parsed.ToString("D")
            : Guid.CreateVersion7().ToString("D");

        correlation.Set(correlationId);
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
