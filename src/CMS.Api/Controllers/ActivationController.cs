using CMS.Application.Wrappers;
using Microsoft.AspNetCore.Mvc;
using CMS.Application.DTOs;
using CMS.Application.Interfaces;

[ApiController]
[Route("api/[controller]")]
public class ActivationController : ControllerBase
{
    private readonly IUserService _userService;

    public ActivationController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyActivationDto dto)
    {
        var tempToken = await _userService.VerifyActivationAsync(dto);
        return Ok(new ApiResponse<string>(tempToken, "Identity verified. Set password now."));
    }

    [HttpPost("complete")]
    public async Task<IActionResult> Complete([FromBody] CompleteActivationDto dto)
    {
        await _userService.CompleteActivationAsync(dto);
        return Ok(new ApiResponse<bool>(true, "Account activated. You may now login."));
    }
}