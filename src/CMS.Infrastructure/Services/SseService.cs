using System.Collections.Concurrent;
using System.Text.Json;
using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CMS.Infrastructure.Services;

public class SseService
{
    private readonly IServiceScopeFactory _scopeFactory;

    // Store active connections. 
    // Key: ConnectionId (Guid)
    // Value: Connection Context
    private readonly ConcurrentDictionary<Guid, SseConnection> _connections = new();

    public SseService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task AddConnectionAsync(HttpContext context, string userId, string role, DashboardFilterDto filter)
    {
        var connectionId = Guid.NewGuid();
        var tcs = new TaskCompletionSource<bool>();

        var connection = new SseConnection
        {
            Id = connectionId,
            Response = context.Response,
            UserId = userId,
            Role = role,
            Filter = filter,
            TaskCompletionSource = tcs
        };

        _connections.TryAdd(connectionId, connection);

        // Send initial data immediately
        await SendUpdateToConnectionAsync(connection);

        // Keep connection open until client disconnects
        try
        {
            await tcs.Task;
        }
        catch
        {
            // Ignore cancellation errors
        }
        finally
        {
            _connections.TryRemove(connectionId, out _);
        }
    }

    public void NotifyDashboardUpdate(string targetRole = "All")
    {
        // Fire and forget - don't block the request that triggered the update
        _ = Task.Run(async () =>
        {
            foreach (var connection in _connections.Values)
            {
                if (targetRole == "All" || connection.Role == targetRole || (targetRole == "Admin" && connection.Role == "Admin"))
                {
                    await SendUpdateToConnectionAsync(connection);
                }
            }
        });
    }

    private async Task SendUpdateToConnectionAsync(SseConnection connection)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dashboardService = scope.ServiceProvider.GetRequiredService<IDashboardService>();

            object? data = null; // Make it nullable

            if (connection.Role == "Admin")
            {
                data = await dashboardService.GetAdminStatsAsync(connection.Filter);
            }
            else if (connection.Role == "DepartmentManager")
            {
                data = await dashboardService.GetManagerStatsAsync(connection.UserId, connection.Filter);
            }

            if (data != null)
            {
                var json = JsonSerializer.Serialize(data);
                await connection.Response.WriteAsync($"data: {json}\n\n");
                await connection.Response.Body.FlushAsync();
            }
        }
        catch
        {
            // If sending fails, assume client disconnected
            connection.TaskCompletionSource.TrySetResult(true);
        }
    }

    private class SseConnection
    {
        public Guid Id { get; set; }
        public required HttpResponse Response { get; set; }
        public required string UserId { get; set; }
        public required string Role { get; set; }
        public required DashboardFilterDto Filter { get; set; }
        public required TaskCompletionSource<bool> TaskCompletionSource { get; set; }
    }
}
