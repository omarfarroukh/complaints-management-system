using System.Security.Claims;
using CMS.Application.DTOs;
using CMS.Application.Wrappers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CMS.Api.Filters;
using CMS.Application.Interfaces;
using CMS.Domain.Entities;
using Microsoft.AspNetCore.Http;

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
            if (!string.IsNullOrEmpty(user.AvatarUrl))
            {
                user.AvatarUrl = $"{Request.Scheme}://{Request.Host}{user.AvatarUrl}";
            }
        }
        return Ok(new ApiResponse<List<UserDto>>(users));
    }

    [HttpPost("{id}/avatar")]
    [InvalidateCache("users", "profiles")]
    public async Task<IActionResult> UploadAvatar(string id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new ApiResponse<string>("No file uploaded."));

        // 1. Check User Role
        var user = await _userService.GetUserByIdAsync(id);
        if (user.Role == "Citizen")
        {
            return BadRequest(new ApiResponse<string>("Admins cannot upload avatars for Citizens."));
        }

        // 2. Upload file
        using var stream = file.OpenReadStream();
        var avatarUrl = await _fileStorageService.SaveFileAsync(stream, file.FileName, "avatars");

        // 3. Update DB
        await _userService.UpdateAvatarAsync(id, avatarUrl);

        // 4. Construct Full URL
        var fullUrl = $"{Request.Scheme}://{Request.Host}{avatarUrl}";

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