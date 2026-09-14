using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Chevrere.Api.Swagger;

public sealed class IdempotencyKeyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? string.Empty;
        if (!path.Contains("inventory", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var method = context.ApiDescription.HttpMethod ?? string.Empty;
        if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
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
}
