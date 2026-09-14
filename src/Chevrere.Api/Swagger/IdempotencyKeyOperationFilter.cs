using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Chevrere.Api.Swagger;

public sealed class IdempotencyKeyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var method = context.ApiDescription.HttpMethod ?? string.Empty;
        if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!RequiresIdempotencyKey(context.ApiDescription.RelativePath ?? string.Empty))
        {
            return;
        }

        operation.Parameters ??= [];
        if (operation.Parameters.Any(p => p.Name == "Idempotency-Key"))
        {
            return;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = true,
            Schema = new OpenApiSchema { Type = "string", MinLength = 8, MaxLength = 128 }
        });
    }

    /// <summary>
    /// Only quantitative mutations need a key. Purchase order lifecycle transitions (approve, cancel)
    /// are naturally idempotent, so they are excluded even though their path contains the collection.
    /// </summary>
    private static bool RequiresIdempotencyKey(string path) =>
        path.Contains("inventory", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith("purchase-orders", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith("receipts", StringComparison.OrdinalIgnoreCase)
        || (path.EndsWith("consumer/orders", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("cancel", StringComparison.OrdinalIgnoreCase));
}
