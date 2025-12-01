using System.Security.Claims;
using CMS.Application.DTOs;
using CMS.Application.Wrappers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CMS.Api.Filters;
using CMS.Application.Interfaces;
using CMS.Api.Helpers;

namespace CMS.Api.Controllers;

/// <summary>
/// User management operations (Admin only)
/// </summary>
[ApiController]
[NotifyDashboard("Admin")]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IFileStorageService _fileStorageService;

    public UsersController(IUserService userService, IFileStorageService fileStorageService)
    {
        _userService = userService;
        _fileStorageService = fileStorageService;
    }

    /// <summary>
    /// Get all users with optional filtering
    /// </summary>
    /// <remarks>
    /// **Authorization:** Admin or DepartmentManager role required
    /// 
    /// Returns a list of all non-citizen users (Employees, Managers, Admins) with optional filtering.
    /// 
    /// **Access Control:**
    /// - Admins: Can view all users across all departments
    /// - Department Managers: Can only view users within their own department
    /// 
    /// **Filtering Options:**
    /// - Role (Employee, DepartmentManager, Admin)
    /// - Department
    /// - Active status (active/inactive users)
    /// - Search by name or email
    /// 
    /// **Response Data:**
    /// - User ID, email, full name
    /// - Role and department
    /// - Active status
    /// - Avatar URL (converted to absolute URL)
    /// 
    /// **Caching:** Results cached for 60 seconds (shared cache across all admin users)
    /// 
    /// **Note:** Citizens are not included in this list. Use separate citizen endpoints if needed.
    /// </remarks>
    /// <param name="filter">Optional filters for role, department, and active status</param>
    /// <returns>List of users matching the filter criteria</returns>
    /// <response code="200">Users retrieved successfully</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not authorized (requires Admin or DepartmentManager role)</response>
    [HttpGet]
    [Authorize(Roles = "Admin,DepartmentManager")]
    [Transactional]
    [Cached(60, "users", IsShared = true)]
    [ProducesResponseType(typeof(ApiResponse<List<UserDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] UserFilterDto filter)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

        var users = await _userService.GetAllUsersAsync(filter, userId, userRole);
        foreach (var user in users)
        {
            user.AvatarUrl = user.AvatarUrl.ToAbsoluteUrl(Request);
        }
        return Ok(new ApiResponse<List<UserDto>>(users));
    }

    /// <summary>
    /// Upload avatar for a user (Admin operation)
    /// </summary>
    /// <remarks>
    /// **Authorization:** Admin role required
    /// 
    /// Allows admins to upload profile avatars for Employees and Managers.
    /// 
    /// **Restrictions:**
    /// - Cannot upload avatars for Citizens (they upload their own)
    /// - Target user must be Employee, Manager, or Admin
    /// 
    /// **File Requirements:**
    /// - Supported formats: JPG, PNG, GIF
    /// - Maximum file size: 5MB
    /// - Image will be automatically resized to standard dimensions
    /// 
    /// **Use Cases:**
    /// - Initial setup of employee profiles
    /// - Updating photos for organizational directories
    /// - Replacing outdated or inappropriate images
    /// 
    /// **Cache Invalidation:** Clears both user and profile caches
    /// </remarks>
    /// <param name="id">User ID to upload avatar for</param>
    /// <param name="file">Avatar image file (multipart/form-data)</param>
    /// <returns>Absolute URL of the uploaded avatar</returns>
    /// <response code="200">Avatar uploaded successfully, returns full URL</response>
    /// <response code="400">No file provided, file too large, or attempting to upload for a Citizen</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not authorized (requires Admin role)</response>
    /// <response code="404">User not found</response>
    [HttpPost("{id}/avatar")]
    [Authorize(Roles = "Admin")]
    // Invalidate the shared list AND the specific profile tag (which implies we wipe the base 'profiles' tag)
    [InvalidateCache("users")] 
    [InvalidateCache("profiles")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadAvatar(string id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new ApiResponse<string>("No file uploaded."));

        var user = await _userService.GetUserByIdAsync(id);
        if (user.Role == "Citizen")
        {
            return BadRequest(new ApiResponse<string>("Admins cannot upload avatars for Citizens."));
        }

        using var stream = file.OpenReadStream();
        var avatarUrl = await _fileStorageService.SaveFileAsync(stream, file.FileName, "avatars");

        await _userService.UpdateAvatarAsync(id, avatarUrl);

        var fullUrl = avatarUrl.ToAbsoluteUrl(Request);

        return Ok(new ApiResponse<string>(fullUrl, "Avatar uploaded successfully"));
    }

    /// <summary>
    /// Create a new Employee account
    /// </summary>
    /// <remarks>
    /// **Authorization:** Admin role required
    /// 
    /// Creates a new Employee account and sends an activation email.
    /// 
    /// **Account Creation Process:**
    /// 1. Admin provides email, department, and profile information
    /// 2. System generates a temporary password
    /// 3. Activation email is sent to the employee
    /// 4. Employee verifies identity using birth date
    /// 5. Employee sets their own password
    /// 
    /// **Required Information:**
    /// - Email (must be unique)
    /// - Department assignment
    /// - Profile: FirstName, LastName, NationalId, BirthDate
    /// 
    /// **Permissions:**
    /// - Employees can view and update complaints in their department
    /// - Cannot assign complaints or manage other users
    /// 
    /// **Cache Invalidation:** Clears user cache
    /// 
    /// **Next Steps:** Employee receives activation email and must complete activation process
    /// </remarks>
    /// <param name="dto">Employee details including email, department, and profile</param>
    /// <returns>Success message with instructions</returns>
    /// <response code="200">Employee created successfully, activation email sent</response>
    /// <response code="400">Invalid input, duplicate email, or validation errors</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not authorized (requires Admin role)</response>
    [HttpPost("employee")]
    [InvalidateCache("users")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateEmployee([FromBody] CreateUserDto dto)
    {
        var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var msg = await _userService.CreateEmployeeAsync(dto, adminId!);
        return Ok(new ApiResponse<bool>(true, msg));
    }

    /// <summary>
    /// Create a new Department Manager account
    /// </summary>
    /// <remarks>
    /// **Authorization:** Admin role required
    /// 
    /// Creates a new Department Manager account and sends an activation email.
    /// 
    /// **Account Creation Process:**
    /// 1. Admin provides email, department, and profile information
    /// 2. System generates a temporary password
    /// 3. Activation email is sent to the manager
    /// 4. Manager verifies identity using birth date
    /// 5. Manager sets their own password
    /// 
    /// **Required Information:**
    /// - Email (must be unique)
    /// - Department assignment (manager will oversee this department)
    /// - Profile: FirstName, LastName, NationalId, BirthDate
    /// 
    /// **Manager Permissions:**
    /// - View all complaints in their department
    /// - Assign complaints to employees
    /// - Update complaint status and priorities
    /// - Manage department resources
    /// 
    /// **Important:** Each department should have one primary manager, though multiple managers can be assigned
    /// 
    /// **Cache Invalidation:** Clears user cache
    /// 
    /// **Transaction:** Uses database transaction to ensure atomicity
    /// </remarks>
    /// <param name="dto">Manager details including email, department, and profile</param>
    /// <returns>Success message with instructions</returns>
    /// <response code="200">Manager created successfully, activation email sent</response>
    /// <response code="400">Invalid input, duplicate email, or validation errors</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not authorized (requires Admin role)</response>
    [HttpPost("manager")]
    [Transactional]
    [InvalidateCache("users")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateManager([FromBody] CreateUserDto dto)
    {
        var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var msg = await _userService.CreateManagerAsync(dto, adminId!);
        return Ok(new ApiResponse<bool>(true, msg));
    }
}