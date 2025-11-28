using CMS.Application.Wrappers;
using Microsoft.AspNetCore.Mvc;
using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using CMS.Api.Filters;
namespace CMS.Api.Controllers;

/// <summary>
/// Account activation operations for Employees and Managers
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ActivationController : ControllerBase
{
    private readonly IUserService _userService;

    public ActivationController(IUserService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// Step 1: Verify identity for account activation
    /// </summary>
    /// <remarks>
    /// **First step of the activation process** for Employees and Managers created by an Admin.
    /// 
    /// **Process:**
    /// 1. User receives activation email with a token
    /// 2. User submits the token and their birth date to verify identity
    /// 3. If successful, a temporary token is returned for the next step
    /// 
    /// **Security:**
    /// - Validates that the provided birth date matches the record
    /// - Activation token must be valid and not expired
    /// </remarks>
    /// <param name="dto">Verification details (Activation Token and Birth Date)</param>
    /// <returns>Temporary token required for setting the password</returns>
    /// <response code="200">Identity verified successfully, temporary token returned</response>
    /// <response code="400">Invalid activation token or birth date mismatch</response>
    /// <response code="404">User not found</response>
    [HttpPost("verify")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Verify([FromBody] VerifyActivationDto dto)
    {
        var tempToken = await _userService.VerifyActivationAsync(dto);
        return Ok(new ApiResponse<string>(tempToken, "Identity verified. Set password now."));
    }

    /// <summary>
    /// Step 2: Complete activation and set password
    /// </summary>
    /// <remarks>
    /// **Second and final step of the activation process.**
    /// 
    /// Uses the temporary token obtained from the verification step to set the user's password and activate the account.
    /// 
    /// **Requirements:**
    /// - Valid temporary token from /verify endpoint
    /// - NewPassword: minimum 8 characters, requires uppercase, lowercase, and digit
    /// - ConfirmPassword must match NewPassword
    /// 
    /// **Outcome:** Account is marked as active and user can log in.
    /// </remarks>
    /// <param name="dto">Activation completion details (Temporary Token and New Password)</param>
    /// <returns>Success confirmation</returns>
    /// <response code="200">Account activated successfully, user can now log in</response>
    /// <response code="400">Invalid token or password validation errors</response>
[   HttpPost("complete")]
    [InvalidateCache("users")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Complete([FromBody] CompleteActivationDto dto)
    {
        await _userService.CompleteActivationAsync(dto);
        return Ok(new ApiResponse<bool>(true, "Account activated. You may now login."));
    }
}