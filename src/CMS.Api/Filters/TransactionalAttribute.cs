using CMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace CMS.Api.Filters;

/// <summary>
/// Action filter that wraps the action execution in a database transaction.
/// Commits on success, rolls back on exception or validation failure.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class TransactionalAttribute : Attribute, IAsyncActionFilter
{
    private readonly int _timeoutSeconds;

    /// <summary>
    /// Creates a new transactional filter
    /// </summary>
    /// <param name="timeoutSeconds">Command timeout in seconds (default: 30)</param>
    public TransactionalAttribute(int timeoutSeconds = 30)
    {
        _timeoutSeconds = timeoutSeconds;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var dbContext = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<TransactionalAttribute>>();

        // Check if transaction already exists (avoid nested transactions)
        if (dbContext.Database.CurrentTransaction != null)
        {
            logger.LogWarning("Transaction already exists, skipping nested transaction creation");
            await next();
            return;
        }

        // Begin transaction
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        
        // Set command timeout
        dbContext.Database.SetCommandTimeout(_timeoutSeconds);
        
        logger.LogDebug("Transaction started with timeout: {TimeoutSeconds}s", _timeoutSeconds);

        try
        {
            // Execute the action (Controller method)
            var executedContext = await next();

            // Check if we should commit
            if (executedContext.Exception == null)
            {
                // Explicitly save changes
                var changes = await dbContext.SaveChangesAsync();
                
                // Commit transaction
                await transaction.CommitAsync();
                
                logger.LogDebug(
                    "Transaction committed successfully. {ChangeCount} changes saved.", 
                    changes);
            }
            else
            {
                // Exception occurred - rollback
                await transaction.RollbackAsync();
                logger.LogWarning(
                    "Transaction rolled back due to exception: {ExceptionType}", 
                    executedContext.Exception.GetType().Name);
            }
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Concurrency conflict detected - transaction rolled back");
            throw;
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Database update failed - transaction rolled back");
            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Transaction failed and rolled back");
            throw;
        }
    }
}