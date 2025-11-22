using CMS.Application.Wrappers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CMS.Api.Filters;

/// <summary>
/// Action filter that validates model state before action execution.
/// Returns a standardized error response for validation failures.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class ValidationFilterAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
        {
            // 1. Extract error messages from the ModelState
            var errors = context.ModelState
                .Where(x => x.Value?.Errors.Any() == true)
                .SelectMany(x => x.Value!.Errors.Select(e => 
                    string.IsNullOrEmpty(e.ErrorMessage) 
                        ? e.Exception?.Message ?? "Validation error" 
                        : e.ErrorMessage))
                .ToList();

            // 2. Wrap them in your Standard Response
            var response = new ApiResponse<string>
            {
                Succeeded = false,
                Message = "Validation failed",
                Errors = errors
            };

            // 3. Return immediately (Short-circuit the pipeline)
            context.Result = new BadRequestObjectResult(response);
        }
    }
}