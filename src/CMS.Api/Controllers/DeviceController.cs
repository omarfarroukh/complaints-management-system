using CMS.Application.Interfaces;
using CMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CMS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DeviceController : ControllerBase
    {
        private readonly IPushNotificationService _pushNotificationService;

        public DeviceController(IPushNotificationService pushNotificationService)
        {
            _pushNotificationService = pushNotificationService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            await _pushNotificationService.RegisterDeviceAsync(userId, request.Token, request.Platform);
            return Ok(new { Message = "Device registered successfully" });
        }

        [HttpPost("unregister")]
        public async Task<IActionResult> UnregisterDevice([FromBody] UnregisterDeviceRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            await _pushNotificationService.UnregisterDeviceAsync(userId, request.Token);
            return Ok(new { Message = "Device unregistered successfully" });
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetDeviceStatus([FromQuery] string token)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // Use the service now
            var isRegistered = await _pushNotificationService.IsDeviceRegisteredAsync(userId, token);

            return Ok(new { IsRegistered = isRegistered });
        }
    }
    public class RegisterDeviceRequest
    {
        public string Token { get; set; } = string.Empty;
        public string Platform { get; set; } = "Android";
    }

    public class UnregisterDeviceRequest
    {
        public string Token { get; set; } = string.Empty;
    }
}
