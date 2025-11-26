using System.Security.Claims;
using CMS.Application.DTOs;
using CMS.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly SseService _sseService;

    public DashboardController(SseService sseService)
    {
        _sseService = sseService;
    }

    /// <summary>
    /// Stream real-time dashboard updates for Admins
    /// </summary>
    /// <param name="from">Filter start date</param>
    /// <param name="to">Filter end date</param>
    [HttpGet("admin/stream")]
    [Authorize(Roles = "Admin")]
    public async Task StreamAdminStats([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var filter = new DashboardFilterDto { From = from, To = to };

        await _sseService.AddConnectionAsync(HttpContext, userId!, "Admin", filter);
    }

    /// <summary>
    /// Stream real-time dashboard updates for Managers
    /// </summary>
    /// <param name="from">Filter start date</param>
    /// <param name="to">Filter end date</param>
    [HttpGet("manager/stream")]
    [Authorize(Roles = "Manager")]
    public async Task StreamManagerStats([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var filter = new DashboardFilterDto { From = from, To = to };

        await _sseService.AddConnectionAsync(HttpContext, userId!, "Manager", filter);
    }
}
