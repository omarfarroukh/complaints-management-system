using System.Net;
using System.Text.Json;
using CMS.Application.Exceptions;
using CMS.Application.Wrappers;
using Microsoft.Extensions.Hosting;

namespace CMS.Api.Middleware;

public class ErrorHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlerMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ErrorHandlerMiddleware(
        RequestDelegate next, 
        ILogger<ErrorHandlerMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _env = env ?? throw new ArgumentNullException(nameof(env));
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception error)
        {
            // Use proper structured logging instead of Console.WriteLine
            _logger.LogError(error, "Unhandled exception occurred");
            
            var response = context.Response;
            response.ContentType = "application/json";
            
            var responseModel = new ApiResponse<object?>(data: null) 
            { 
                Succeeded = false 
            };

            switch (error)
            {
                case ApiException apiEx:
                    // Custom application error
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    responseModel.Message = apiEx.Message;
                    // Include errors if available
                    if (apiEx.Errors != null && apiEx.Errors.Count > 0)
                    {
                        responseModel.Errors = apiEx.Errors;
                    }
                    break;
                
                case KeyNotFoundException:
                    // Not found error
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    responseModel.Message = "Resource not found";
                    break;

                case UnauthorizedAccessException:
                    // Access denied
                    response.StatusCode = (int)HttpStatusCode.Forbidden;
                    responseModel.Message = "Access denied";
                    break;

                case ArgumentException argEx:
                    // Invalid argument
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    responseModel.Message = _env.IsDevelopment() 
                        ? argEx.Message 
                        : "Invalid request parameters";
                    break;

                default:
                    // Unhandled error
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    // Hide details in production for security
                    responseModel.Message = _env.IsDevelopment() 
                        ? error.Message 
                        : "An internal server error occurred";
                    break;
            }

            var options = new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            
            var result = JsonSerializer.Serialize(responseModel, options);
            await response.WriteAsync(result);
        }
    }
}