using CMS.Application.DTOs;

namespace CMS.Application.Interfaces
{
    public interface IUserService
    {
        Task<List<UserDto>> GetAllUsersAsync(UserFilterDto filter, string? userId = null, string? userRole = null);
        Task<UserDto> GetUserByIdAsync(string id);

        // User Creation
        Task<string> CreateEmployeeAsync(CreateUserDto dto, string adminId);
        Task<string> CreateManagerAsync(CreateUserDto dto, string adminId);
        Task ToggleStatusAsync(string id, string adminId);

        // Profile Logic
        Task<UserProfileDto> GetProfileAsync(string userId);
        Task UpdateProfileAsync(string userId, UpdateProfileDto dto);
        Task UpdateAvatarAsync(string userId, string? avatarUrl);

        // Activation Logic
        Task<string> VerifyActivationAsync(VerifyActivationDto dto);
        Task CompleteActivationAsync(CompleteActivationDto dto);
    }
}