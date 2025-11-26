using System.Security.Claims;
using CMS.Application.DTOs;
using CMS.Application.Wrappers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CMS.Application.Interfaces;
using CMS.Api.Filters;
using CMS.Api.Helpers;

namespace CMS.Api.Controllers
{
    /// <summary>
    /// User profile management operations
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ProfileController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IFileStorageService _fileStorageService;

        public ProfileController(IUserService userService, IFileStorageService fileStorageService)
        {
            _userService = userService;
            _fileStorageService = fileStorageService;
        }

        /// <summary>
        /// Get the authenticated user's profile
        /// </summary>
        /// <remarks>
        /// **Authorization:** Any authenticated user
        /// 
        /// Returns the current user's complete profile information including:
        /// - First and last name
        /// - National ID (if provided)
        /// - Birth date
        /// - Contact information (address, city, country)
        /// - Avatar URL (converted to absolute URL)
        /// 
        /// **Caching:** Results cached for 300 seconds (5 minutes) per user
        /// 
        /// **Use Cases:**
        /// - Display user information in the UI
        /// - Pre-fill forms with existing data
        /// - Profile page display
        /// </remarks>
        /// <returns>User profile information</returns>
        /// <response code="200">Profile retrieved successfully</response>
        /// <response code="401">Not authenticated or invalid token</response>
        [HttpGet]
        [Cached(300, "profiles")]
        [ProducesResponseType(typeof(ApiResponse<UserProfileDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetMyProfile()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var profile = await _userService.GetProfileAsync(userId!);

            // Convert relative avatar URL to absolute URL
            profile.AvatarUrl = profile.AvatarUrl.ToAbsoluteUrl(Request);

            return Ok(new ApiResponse<UserProfileDto>(profile));
        }

        /// <summary>
        /// Update the authenticated user's profile
        /// </summary>
        /// <remarks>
        /// **Authorization:** Any authenticated user
        /// 
        /// Allows users to update their own profile information. This is a partial update (PATCH) - only provided fields will be updated.
        /// 
        /// **Updatable Fields:**
        /// - FirstName
        /// - LastName
        /// - NationalId
        /// - BirthDate
        /// - Address
        /// - City
        /// - Country
        /// 
        /// **Partial Update Behavior:**
        /// - Only include fields you want to update in the request
        /// - Null or missing fields will not be changed
        /// - Empty strings will clear the field value
        /// 
        /// **Validation:**
        /// - FirstName and LastName must be valid if provided
        /// - BirthDate must be in the past
        /// - NationalId format may be validated depending on country
        /// 
        /// **Cache Invalidation:** Clears profile cache for this user
        /// 
        /// **Transaction:** Uses database transaction to ensure atomicity
        /// </remarks>
        /// <param name="dto">Profile fields to update (only include fields that should change)</param>
        /// <returns>Success confirmation</returns>
        /// <response code="200">Profile updated successfully</response>
        /// <response code="400">Invalid field values or validation errors</response>
        /// <response code="401">Not authenticated or invalid token</response>
        [HttpPatch]
        [Transactional]
        [InvalidateCache("profiles")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            await _userService.UpdateProfileAsync(userId!, dto);
            return Ok(new ApiResponse<bool>(true, "Profile updated successfully"));
        }

        /// <summary>
        /// Upload a profile avatar image
        /// </summary>
        /// <remarks>
        /// **Authorization:** Any authenticated user
        /// 
        /// Allows users to upload or update their profile avatar/photo.
        /// 
        /// **File Requirements:**
        /// - Supported formats: JPG, JPEG, PNG, GIF
        /// - Maximum file size: 5MB
        /// - Recommended dimensions: 200x200 pixels (square)
        /// - Images will be automatically resized if too large
        /// 
        /// **Storage:**
        /// - Files are stored securely on the server
        /// - Previous avatar is replaced (old file is deleted)
        /// - URL is converted to absolute path for easy client access
        /// 
        /// **Use Cases:**
        /// - Initial profile setup
        /// - Updating profile photo
        /// - Personalizing user account
        /// 
        /// **Cache Invalidation:** Clears profile cache to show new avatar immediately
        /// 
        /// **Note:** Citizens upload their own avatars; Admins can upload for Employees/Managers via /api/users/{id}/avatar
        /// </remarks>
        /// <param name="file">Avatar image file (multipart/form-data)</param>
        /// <returns>Absolute URL of the uploaded avatar</returns>
        /// <response code="200">Avatar uploaded successfully, returns full URL</response>
        /// <response code="400">No file provided, file too large, or unsupported format</response>
        /// <response code="401">Not authenticated or invalid token</response>
        [HttpPost("avatar")]
        [InvalidateCache("profiles")]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> UploadAvatar(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new ApiResponse<string>("No file uploaded."));

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            using var stream = file.OpenReadStream();
            var avatarUrl = await _fileStorageService.SaveFileAsync(stream, file.FileName, "avatars");

            await _userService.UpdateAvatarAsync(userId!, avatarUrl);

            // Convert to absolute URL for client use
            var fullUrl = avatarUrl.ToAbsoluteUrl(Request);

            return Ok(new ApiResponse<string>(fullUrl, "Avatar uploaded successfully"));
        }

        /// <summary>
        /// Delete the user's profile avatar
        /// </summary>
        /// <remarks>
        /// **Authorization:** Any authenticated user
        /// 
        /// Removes the user's current profile avatar and deletes the associated file from storage.
        /// 
        /// **Behavior:**
        /// - Deletes avatar file from server storage
        /// - Sets avatar URL to null in database
        /// - User's profile will show default/placeholder avatar
        /// - Safe to call even if no avatar exists (idempotent)
        /// 
        /// **Use Cases:**
        /// - User wants to remove their photo
        /// - Replacing with a new avatar (though uploading will overwrite)
        /// - Privacy concerns
        /// - Reverting to default avatar
        /// 
        /// **Cache Invalidation:** Clears profile cache to reflect removal immediately
        /// 
        /// **Note:** This does not delete the user account, only the avatar image
        /// </remarks>
        /// <returns>Success confirmation</returns>
        /// <response code="200">Avatar deleted successfully (or no avatar to delete)</response>
        /// <response code="401">Not authenticated or invalid token</response>
        [HttpDelete("avatar")]
        [InvalidateCache("profiles")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> DeleteAvatar()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var profile = await _userService.GetProfileAsync(userId!);

            if (!string.IsNullOrEmpty(profile.AvatarUrl))
            {
                await _fileStorageService.DeleteFileAsync(profile.AvatarUrl);
                await _userService.UpdateAvatarAsync(userId!, null);
            }

            return Ok(new ApiResponse<bool>(true, "Avatar deleted successfully"));
        }
    }
}