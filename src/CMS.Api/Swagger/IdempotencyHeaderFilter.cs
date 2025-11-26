using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using CMS.Api.Filters;

namespace CMS.Api.Swagger;

/// <summary>
/// Swagger operation filter that adds X-Idempotency-Key header parameter
/// to endpoints decorated with [Idempotency] attribute
/// </summary>
public class IdempotencyHeaderFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Check if the endpoint has the IdempotencyAttribute
        var hasIdempotencyAttribute = context.MethodInfo
            .GetCustomAttributes(true)
            .Any(attr => attr is IdempotencyAttribute);

        if (hasIdempotencyAttribute)
        {
            operation.Parameters ??= new List<OpenApiParameter>();

            // Add the X-Idempotency-Key header parameter
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "Idempotency-Key",
                In = ParameterLocation.Header,
                Required = true,
                Description = "Unique identifier to ensure idempotent request processing. Use a UUID or GUID format.",
                Schema = new OpenApiSchema
                {
                    Type = "string",
                    Format = "uuid",
                    Example = new Microsoft.OpenApi.Any.OpenApiString(Guid.NewGuid().ToString())
                }
            });
        }
    }
}
