using System.Security.Claims;
using CMS.Application.DTOs;
using CMS.Application.Wrappers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CMS.Api.Filters;
using CMS.Application.Interfaces;
using CMS.Api.Helpers; // Added

namespace CMS.Api.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
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

    [HttpGet]
    [Transactional]
    [Cached(60, "users", IsShared = true)]
    public async Task<IActionResult> GetAll([FromQuery] UserFilterDto filter)
    {
        var users = await _userService.GetAllUsersAsync(filter);
        foreach (var user in users)
        {
            // Simplified using Extension Method
            user.AvatarUrl = user.AvatarUrl.ToAbsoluteUrl(Request);
        }
        return Ok(new ApiResponse<List<UserDto>>(users));
    }

    [HttpPost("{id}/avatar")]
    [InvalidateCache("users", "profiles")]
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

        // Simplified using Extension Method
        var fullUrl = avatarUrl.ToAbsoluteUrl(Request);

        return Ok(new ApiResponse<string>(fullUrl, "Avatar uploaded successfully"));
    }

    [HttpPost("employee")]
    [InvalidateCache("users")]
    public async Task<IActionResult> CreateEmployee([FromBody] CreateUserDto dto)
    {
        var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var msg = await _userService.CreateEmployeeAsync(dto, adminId!);
        return Ok(new ApiResponse<string>(msg));
    }

    [HttpPost("manager")]
    [Transactional]
    [InvalidateCache("users")]
    public async Task<IActionResult> CreateManager([FromBody] CreateUserDto dto)
    {
        var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var msg = await _userService.CreateManagerAsync(dto, adminId!);
        return Ok(new ApiResponse<string>(msg));
    }
}