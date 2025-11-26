$programFile = "d:\asp.net\src\CMS.Api\Program.cs"
$content = Get-Content $programFile -Raw

# Add using statement for System.Reflection
$content = $content.Replace(
    "using System.Text;`r`nusing CMS.Infrastructure.BackgroundJobs;",
    "using System.Text;`r`nusing System.Reflection;`r`nusing CMS.Infrastructure.BackgroundJobs;"
)

# Update Swagger configuration 
$oldSwaggerConfig = @'
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "CMS API",
            Version = "v1",
            Description = "Content Management System API with AOP security features"
        });

        // JWT Bearer Authentication in Swagger
'@

$newSwaggerConfig = @'
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "CMS API",
            Version = "v1",
            Description = "Content Management System API with comprehensive documentation, AOP security features, and idempotency support",
            Contact = new OpenApiContact
            {
                Name = "CMS Development Team",
                Email = "support@cms-api.com"
            }
        });

        // Enable XML documentation comments
        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            c.IncludeXml Comments(xmlPath);
        }

        // Add custom filters
        c.SchemaFilter<CMS.Api.Swagger.DateTimeExampleFilter>();
        c.OperationFilter<CMS.Api.Swagger.IdempotencyHeaderFilter>();

        // JWT Bearer Authentication in Swagger
'@

$content = $content.Replace($oldSwaggerConfig, $newSwaggerConfig)

# Save the file 
Set-Content -Path $programFile -Value $content -NoNewline

Write-Host "Program.cs updated successfully!"
