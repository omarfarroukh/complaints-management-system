using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CMS.Api.Swagger;

/// <summary>
/// Swagger schema filter that provides example values for DateTime properties
/// </summary>
public class DateTimeExampleFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(DateTime) || context.Type == typeof(DateTime?))
        {
            schema.Example = new OpenApiString(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        }
    }
}
