using System.Security.Claims;
using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using CMS.Application.Wrappers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CMS.Api.Filters;

namespace CMS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    [Transactional]
    [InvalidateCache("users")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        // No try-catch needed. Middleware handles errors.
        var message = await _authService.RegisterAsync(dto);
        return Ok(new ApiResponse<string>(message, "User registered successfully"));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var result = await _authService.LoginAsync(dto);
        return Ok(new ApiResponse<AuthResponseDto>(result, "Login Successful"));
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenDto dto)
    {
        var result = await _authService.RefreshTokenAsync(dto);
        return Ok(new ApiResponse<AuthResponseDto>(result, "Token refreshed"));
    }

    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailDto dto)
    {
        await _authService.ConfirmEmailAsync(dto);
        return Ok(new ApiResponse<bool>(true, "Email confirmed successfully"));
    }

    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation([FromBody] ForgotPasswordDto dto)
    {
        // Reuse ForgotPasswordDto since it only contains Email
        await _authService.ResendConfirmationEmailAsync(dto.Email);
        return Ok(new ApiResponse<bool>(true, "If the email exists, a confirmation link has been sent."));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        await _authService.ForgotPasswordAsync(dto.Email);
        return Ok(new ApiResponse<bool>(true, "If the email exists, a reset link has been sent."));
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        await _authService.ResetPasswordAsync(dto);
        return Ok(new ApiResponse<bool>(true, "Password reset successfully"));
    }

    [Authorize] // <--- Requires JWT Header
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        // 1. Get User ID securely from the Token
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse<string>("User ID missing from token"));

        // 2. Call Service
        await _authService.ChangePasswordAsync(userId, dto);
        return Ok(new ApiResponse<bool>(true, "Password changed successfully"));
    }


    [Authorize]
    [HttpGet("mfa-setup")]
    public async Task<IActionResult> MfaSetup()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var result = await _authService.GenerateMfaSetupAsync(userId!);
        return Ok(new ApiResponse<MfaSetupResponseDto>(result));
    }

    [Authorize]
    [HttpPost("mfa-enable")]
    public async Task<IActionResult> MfaEnable([FromBody] MfaVerifyDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await _authService.EnableMfaAsync(userId!, dto.Code);
        return Ok(new ApiResponse<bool>(true, "MFA Enabled Successfully"));
    }

    [Authorize]
    [HttpPost("mfa-disable")]
    public async Task<IActionResult> MfaDisable()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await _authService.DisableMfaAsync(userId!);
        return Ok(new ApiResponse<bool>(true, "MFA Disabled"));
    }
}

