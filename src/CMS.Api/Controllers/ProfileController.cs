using System.Security.Claims;
using CMS.Application.DTOs;
using CMS.Application.Wrappers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CMS.Application.Interfaces;
using CMS.Api.Filters;
using Microsoft.AspNetCore.Http;

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

            if (!string.IsNullOrEmpty(profile.AvatarUrl))
            {
                profile.AvatarUrl = $"{Request.Scheme}://{Request.Host}{profile.AvatarUrl}";
            }

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

            // 1. Upload file
            using var stream = file.OpenReadStream();
            var avatarUrl = await _fileStorageService.SaveFileAsync(stream, file.FileName, "avatars");

            // 2. Update DB
            await _userService.UpdateAvatarAsync(userId!, avatarUrl);

            // 3. Construct Full URL
            var fullUrl = $"{Request.Scheme}://{Request.Host}{avatarUrl}";

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
                // 1. Delete file
                await _fileStorageService.DeleteFileAsync(profile.AvatarUrl);

                // 2. Update DB
                await _userService.UpdateAvatarAsync(userId!, null);
            }

            return Ok(new ApiResponse<bool>(true, "Avatar deleted successfully"));
        }
    }
}
