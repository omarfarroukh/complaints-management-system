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
[NotifyDashboard("Admin")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    // Helper to get IP from HTTP Context
    private string GetIpAddress()
    {
        if (Request.Headers.ContainsKey("X-Forwarded-For"))
            return Request.Headers["X-Forwarded-For"].ToString();

        return HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "Unknown";
    }

    /// <summary>
    /// Register a new user account
    /// </summary>
    /// <remarks>
    /// Creates a new user account (Citizen, Employee, or Manager) and sends a confirmation email.
    /// 
    /// **Requirements:**
    /// - Email must be unique
    /// - Password: minimum 8 characters, requires uppercase, lowercase, and digit
    /// - ConfirmPassword must match Password
    /// - Profile information (FirstName, LastName) must be complete
    /// 
    /// **Next Steps:** User must confirm their email before they can log in
    /// </remarks>
    /// <param name="dto">Registration details including email, password, user type, and profile information</param>
    /// <returns>Success message indicating registration was successful</returns>
    /// <response code="200">User registered successfully, confirmation email sent</response>
    /// <response code="400">Invalid input data or validation errors (duplicate email, weak password, etc.)</response>
    [HttpPost("register")]
    [Transactional]
    [InvalidateCache("users")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        var message = await _authService.RegisterAsync(dto);
        return Ok(new ApiResponse<string>(message, "User registered successfully"));
    }

    /// <summary>
    /// Authenticate a user and obtain JWT tokens
    /// </summary>
    /// <remarks>
    /// Authenticates user credentials and returns access and refresh tokens.
    /// 
    /// **Security Features:**
    /// - Tracks login attempts by IP address
    /// - Implements automatic rate limiting
    /// - Blocks IPs after repeated failed attempts
    /// - Supports optional Multi-Factor Authentication (MFA)
    /// 
    /// **Requirements:**
    /// - Email must be confirmed
    /// - Account must not be locked
    /// - Provide MFA code if MFA is enabled on the account
    /// </remarks>
    /// <param name="dto">Login credentials (email, password, and optional MFA code)</param>
    /// <returns>JWT access token, refresh token, and basic user information</returns>
    /// <response code="200">Login successful, tokens returned</response>
    /// <response code="400">Invalid credentials or validation errors</response>
    /// <response code="401">Email not confirmed or invalid credentials</response>
    /// <response code="403">Account locked due to repeated failed login attempts</response>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        // Pass IP Address to Service here
        var ip = GetIpAddress();
        var result = await _authService.LoginAsync(dto, ip);
        return Ok(new ApiResponse<AuthResponseDto>(result, "Login Successful"));
    }

    /// <summary>
    /// Refresh an expired access token using a refresh token
    /// </summary>
    /// <remarks>
    /// Exchanges a valid refresh token for a new access token and refresh token pair.
    /// 
    /// **Use Case:** When the access token expires, use this endpoint to obtain a new one without requiring the user to log in again.
    /// 
    /// **Security:** Refresh tokens are single-use and invalidated after successful refresh.
    /// </remarks>
    /// <param name="dto">The refresh token obtained from the login response</param>
    /// <returns>New JWT access token and refresh token</returns>
    /// <response code="200">Token refreshed successfully</response>
    /// <response code="400">Invalid or expired refresh token</response>
    [HttpPost("refresh-token")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenDto dto)
    {
        var result = await _authService.RefreshTokenAsync(dto);
        return Ok(new ApiResponse<AuthResponseDto>(result, "Token refreshed"));
    }

    /// <summary>
    /// Confirm a user's email address
    /// </summary>
    /// <remarks>
    /// Validates the email confirmation token sent to the user's email address during registration.
    /// 
    /// **Required Before:** User can log in
    /// 
    /// **Token Validity:** Confirmation tokens expire after a set period (typically 24 hours)
    /// </remarks>
    /// <param name="dto">Email address and confirmation token from the email</param>
    /// <returns>Success confirmation message</returns>
    /// <response code="200">Email confirmed successfully, user can now log in</response>
    /// <response code="400">Invalid or expired token</response>
    [HttpPost("confirm-email")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailDto dto)
    {
        await _authService.ConfirmEmailAsync(dto);
        return Ok(new ApiResponse<bool>(true, "Email confirmed successfully"));
    }

    /// <summary>
    /// Resend email confirmation link
    /// </summary>
    /// <remarks>
    /// Sends a new confirmation email if the user didn't receive the original or if it expired.
    /// 
    /// **Security:** Response is intentionally vague to prevent email enumeration attacks.
    /// Success message is returned regardless of whether the email exists.
    /// </remarks>
    /// <param name="dto">Email address to send confirmation to</param>
    /// <returns>Generic success message</returns>
    /// <response code="200">If the email exists, a new confirmation link has been sent</response>
    [HttpPost("resend-confirmation")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendConfirmation([FromBody] ForgotPasswordDto dto)
    {
        await _authService.ResendConfirmationEmailAsync(dto.Email);
        return Ok(new ApiResponse<bool>(true, "If the email exists, a confirmation link has been sent."));
    }

    /// <summary>
    /// Request a password reset link
    /// </summary>
    /// <remarks>
    /// Sends a password reset link to the user's email address if the account exists.
    /// 
    /// **Security:** Response is intentionally vague to prevent email enumeration attacks.
    /// Success message is returned regardless of whether the email exists.
    /// 
    /// **Token Validity:** Reset tokens expire after a set period (typically 1 hour)
    /// </remarks>
    /// <param name="dto">Email address to send password reset link to</param>
    /// <returns>Generic success message</returns>
    /// <response code="200">If the email exists, a password reset link has been sent</response>
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        await _authService.ForgotPasswordAsync(dto.Email);
        return Ok(new ApiResponse<bool>(true, "If the email exists, a reset link has been sent."));
    }

    /// <summary>
    /// Reset password using a reset token
    /// </summary>
    /// <remarks>
    /// Resets the user's password using the token from the password reset email.
    /// 
    /// **Requirements:**
    /// - Valid reset token from email
    /// - NewPassword: minimum 8 characters, requires uppercase, lowercase, and digit
    /// - ConfirmPassword must match NewPassword
    /// 
    /// **Token Expiry:** Reset tokens are single-use and expire after a set period
    /// </remarks>
    /// <param name="dto">Email, reset token, and new password</param>
    /// <returns>Success confirmation message</returns>
    /// <response code="200">Password reset successfully, user can now log in with new password</response>
    /// <response code="400">Invalid or expired token, or password validation errors</response>
    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        await _authService.ResetPasswordAsync(dto);
        return Ok(new ApiResponse<bool>(true, "Password reset successfully"));
    }

    /// <summary>
    /// Change password for authenticated user
    /// </summary>
    /// <remarks>
    /// Allows an authenticated user to change their password.
    /// 
    /// **Authorization:** Requires valid JWT token
    /// 
    /// **Requirements:**
    /// - OldPassword must be correct
    /// - NewPassword: minimum 8 characters, requires uppercase, lowercase, and digit
    /// - ConfirmPassword must match NewPassword
    /// - NewPassword must be different from OldPassword
    /// </remarks>
    /// <param name="dto">Old password and new password</param>
    /// <returns>Success confirmation message</returns>
    /// <response code="200">Password changed successfully</response>
    /// <response code="400">Invalid old password or password validation errors</response>
    /// <response code="401">Not authenticated or invalid token</response>
    [Authorize]
    [HttpPost("change-password")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse<string>("User ID missing from token"));

        await _authService.ChangePasswordAsync(userId, dto);
        return Ok(new ApiResponse<bool>(true, "Password changed successfully"));
    }

    /// <summary>
    /// Generate MFA (Multi-Factor Authentication) setup information
    /// </summary>
    /// <remarks>
    /// Generates a new MFA secret and QR code for the authenticated user to scan with an authenticator app.
    /// 
    /// **Authorization:** Requires valid JWT token
    /// 
    /// **Process:**
    /// 1. Call this endpoint to get QR code
    /// 2. Scan QR code with authenticator app (Google Authenticator, Authy, etc.)
    /// 3. Call /mfa-enable with a code from the app to activate MFA
    /// 
    /// **Note:** MFA is not enabled until /mfa-enable is successfully called
    /// </remarks>
    /// <returns>MFA secret and QR code URI for authenticator app</returns>
    /// <response code="200">MFA setup information generated successfully</response>
    /// <response code="401">Not authenticated or invalid token</response>
    [Authorize]
    [HttpGet("mfa-setup")]
    [ProducesResponseType(typeof(ApiResponse<MfaSetupResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MfaSetup()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var result = await _authService.GenerateMfaSetupAsync(userId!);
        return Ok(new ApiResponse<MfaSetupResponseDto>(result));
    }

    /// <summary>
    /// Enable MFA (Multi-Factor Authentication) for the user
    /// </summary>
    /// <remarks>
    /// Activates MFA for the authenticated user by verifying the code from their authenticator app.
    /// 
    /// **Authorization:** Requires valid JWT token
    /// 
    /// **Prerequisites:** Must call /mfa-setup first to generate secret and QR code
    /// 
    /// **After Enabling:** User will be required to provide MFA code during login
    /// </remarks>
    /// <param name="dto">6-digit code from authenticator app</param>
    /// <returns>Success confirmation message</returns>
    /// <response code="200">MFA enabled successfully, future logins will require MFA code</response>
    /// <response code="400">Invalid MFA code</response>
    /// <response code="401">Not authenticated or invalid token</response>
    [Authorize]
    [HttpPost("mfa-enable")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MfaEnable([FromBody] MfaVerifyDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await _authService.EnableMfaAsync(userId!, dto.Code);
        return Ok(new ApiResponse<bool>(true, "MFA Enabled Successfully"));
    }

    /// <summary>
    /// Disable MFA (Multi-Factor Authentication) for the user
    /// </summary>
    /// <remarks>
    /// Deactivates MFA for the authenticated user.
    /// 
    /// **Authorization:** Requires valid JWT token
    /// 
    /// **After Disabling:** User will no longer be required to provide MFA code during login
    /// 
    /// **Security:** Consider requiring password confirmation before allowing MFA disable
    /// </remarks>
    /// <returns>Success confirmation message</returns>
    /// <response code="200">MFA disabled successfully</response>
    /// <response code="401">Not authenticated or invalid token</response>
    [Authorize]
    [HttpPost("mfa-disable")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MfaDisable()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await _authService.DisableMfaAsync(userId!);
        return Ok(new ApiResponse<bool>(true, "MFA Disabled"));
    }

    /// <summary>
    /// Check if an email is already used
    /// </summary>
    /// <remarks>
    /// Validates if the provided email is already registered in the system.
    /// 
    /// **Use Case:** Before allowing a new user to register, verify that the email is not already in use.
    /// 
    /// **Response:** Returns a boolean indicating whether the email is used or not.
    /// </remarks>
    /// <param name="email">Email address to check</param>
    /// <returns>Boolean indicating if the email is used</returns>
    /// <response code="200">Email check result</response>
    [HttpGet("check-email")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckEmail(string email)
    {
        var isUsed = await _authService.IsEmailUsedAsync(email);
        return Ok(new ApiResponse<bool>(isUsed, "Email check result"));
    }
}