using System.Security.Claims;
using CMS.Application.DTOs;
using CMS.Application.Wrappers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CMS.Application.Interfaces;
using CMS.Api.Filters;
using CMS.Api.Helpers; // Added

namespace CMS.Api.Controllers
{
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

        [HttpGet]
        [Cached(300, "profiles")]
        public async Task<IActionResult> GetMyProfile()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var profile = await _userService.GetProfileAsync(userId!);

            // Simplified using Extension Method
            profile.AvatarUrl = profile.AvatarUrl.ToAbsoluteUrl(Request);

            return Ok(new ApiResponse<UserProfileDto>(profile));
        }

        [HttpPatch]
        [Transactional]
        [InvalidateCache("profiles")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            await _userService.UpdateProfileAsync(userId!, dto);
            return Ok(new ApiResponse<bool>(true, "Profile updated successfully"));
        }

        [HttpPost("avatar")]
        [InvalidateCache("profiles")]
        public async Task<IActionResult> UploadAvatar(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new ApiResponse<string>("No file uploaded."));

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            using var stream = file.OpenReadStream();
            var avatarUrl = await _fileStorageService.SaveFileAsync(stream, file.FileName, "avatars");

            await _userService.UpdateAvatarAsync(userId!, avatarUrl);

            // Simplified using Extension Method
            var fullUrl = avatarUrl.ToAbsoluteUrl(Request);

            return Ok(new ApiResponse<string>(fullUrl, "Avatar uploaded successfully"));
        }

        [HttpDelete("avatar")]
        [InvalidateCache("profiles")]
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