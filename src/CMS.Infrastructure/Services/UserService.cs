using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using CMS.Application.Exceptions;
using CMS.Domain.Entities;
using CMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration; // Added
using Hangfire;

namespace CMS.Infrastructure.Services
{
    public class UserService : IUserService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;
        private readonly IDistributedCache _cache;
        private readonly IConfiguration _configuration; // Added

        public UserService(
            UserManager<ApplicationUser> userManager,
            AppDbContext context,
            IEmailService emailService,
            IDistributedCache cache,
            IConfiguration configuration) // Added
        {
            _userManager = userManager;
            _context = context;
            _emailService = emailService;
            _cache = cache;
            _configuration = configuration;
        }

        // [GetAllUsersAsync and GetUserByIdAsync remain unchanged]
        public async Task<List<UserDto>> GetAllUsersAsync(UserFilterDto filter, string? userId = null, string? userRole = null)
        {
            var query = _context.Users.Include(u => u.Profile).AsQueryable();

            // If the caller is a department manager, restrict to their department
            if (userRole == "DepartmentManager" && !string.IsNullOrEmpty(userId))
            {
                var manager = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (manager?.Department != null)
                {
                    query = query.Where(u => u.Department == manager.Department);
                }
            }

            if (!string.IsNullOrEmpty(userId))
            {
                query = query.Where(u => u.Id != userId);
            }

            if (!string.IsNullOrEmpty(filter.SearchTerm))
            {
                var term = filter.SearchTerm.ToLower();
                query = query.Where(u => u.Email!.ToLower().Contains(term) ||
                                        (u.Profile != null && (u.Profile.FirstName.ToLower().Contains(term) || u.Profile.LastName.ToLower().Contains(term))));
            }

            if (!string.IsNullOrEmpty(filter.Role) && Enum.TryParse<UserType>(filter.Role, true, out var role))
            {
                query = query.Where(u => u.UserType == role);
            }

            if (!string.IsNullOrEmpty(filter.Department) && Enum.TryParse<Department>(filter.Department, true, out var dept))
            {
                query = query.Where(u => u.Department == dept);
            }

            if (filter.IsActive.HasValue)
            {
                query = query.Where(u => u.IsActive == filter.IsActive.Value);
            }

            query = filter.SortBy?.ToLower() switch
            {
                "email" => filter.IsDescending ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
                "role" => filter.IsDescending ? query.OrderByDescending(u => u.UserType) : query.OrderBy(u => u.UserType),
                _ => query.OrderByDescending(u => u.Id)
            };

            return await query
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Email = u.Email!,
                    FullName = u.Profile != null ? $"{u.Profile.FirstName} {u.Profile.LastName}" : u.Email!,
                    Role = u.UserType.ToString(),
                    Department = u.Department.HasValue ? u.Department.ToString()! : "N/A",
                    IsActive = u.IsActive,
                    AvatarUrl = u.Profile != null ? u.Profile.AvatarUrl : null
                })
                .ToListAsync();
        }

        public async Task<UserDto> GetUserByIdAsync(string id)
        {
            var user = await _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) throw new ApiException("User not found");

            return new UserDto
            {
                Id = user.Id,
                Email = user.Email!,
                FullName = user.Profile != null ? $"{user.Profile.FirstName} {user.Profile.LastName}" : user.Email!,
                Role = user.UserType.ToString(),
                IsActive = user.IsActive
            };
        }

        public async Task<string> CreateEmployeeAsync(CreateUserDto dto, string adminId)
        {
            return await CreateUserInternal(dto, UserType.Employee);
        }

        public async Task<string> CreateManagerAsync(CreateUserDto dto, string adminId)
        {
            if (dto.Department == null) throw new ApiException("Managers must be assigned a department.");
            return await CreateUserInternal(dto, UserType.DepartmentManager);
        }

        private async Task<string> CreateUserInternal(CreateUserDto dto, UserType type)
        {
            var user = new ApplicationUser
            {
                UserName = dto.Email,
                Email = dto.Email,
                UserType = type,
                Department = dto.Department,
                IsActive = false
            };

            var result = await _userManager.CreateAsync(user);
            if (!result.Succeeded) throw new ApiException(string.Join(",", result.Errors.Select(e => e.Description)));

            var profile = new UserProfile
            {
                UserId = user.Id,
                FirstName = dto.Profile.FirstName,
                LastName = dto.Profile.LastName,
                NationalId = dto.Profile.NationalId,
                BirthDate = dto.Profile.BirthDate,
                Address = dto.Profile.Address,
                City = dto.Profile.City,
                Country = dto.Profile.Country,
            };
            _context.UserProfiles.Add(profile);

            var tokenStr = Guid.NewGuid().ToString("N");
            var activationToken = new AccountActivationToken
            {
                UserId = user.Id,
                Token = tokenStr,
                ExpiresAt = DateTime.UtcNow.AddHours(48)
            };
            _context.AccountActivationTokens.Add(activationToken);

            await _context.SaveChangesAsync();

            // FIX: Use Configuration for base URL
            var baseUrl = _configuration["ClientApp:BaseUrl"] ?? "http://localhost:4200";
            var link = $"{baseUrl}/auth/activate?token={tokenStr}";

            BackgroundJob.Enqueue<IEmailService>(x => x.SendEmailAsync(user.Email!, "Activate Account", $"Link: {link}"));

            return "User created. Activation email sent.";
        }

        public async Task ToggleStatusAsync(string id, string adminId)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) throw new ApiException("User not found");
            if (user.Id == adminId) throw new ApiException("Cannot deactivate yourself");

            user.IsActive = !user.IsActive;
            await _userManager.UpdateAsync(user);
        }

        public async Task<UserProfileDto> GetProfileAsync(string userId)
        {
            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            if (profile == null) throw new ApiException("Profile not found");

            return new UserProfileDto
            {
                FirstName = profile.FirstName,
                LastName = profile.LastName,
                NationalId = profile.NationalId,
                Address = profile.Address,
                City = profile.City,
                Country = profile.Country,
                BirthDate = profile.BirthDate,
                AvatarUrl = profile.AvatarUrl
            };
        }

        public async Task UpdateProfileAsync(string userId, UpdateProfileDto dto)
        {
            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            if (profile == null)
            {
                profile = new UserProfile { UserId = userId };
                _context.UserProfiles.Add(profile);
            }

            if (!string.IsNullOrEmpty(dto.FirstName)) profile.FirstName = dto.FirstName;
            if (!string.IsNullOrEmpty(dto.LastName)) profile.LastName = dto.LastName;
            if (dto.NationalId != null) profile.NationalId = dto.NationalId;
            if (dto.BirthDate != null) profile.BirthDate = dto.BirthDate;
            if (dto.Address != null) profile.Address = dto.Address;
            if (dto.City != null) profile.City = dto.City;
            if (dto.Country != null) profile.Country = dto.Country;

            await _context.SaveChangesAsync();
        }

        public async Task UpdateAvatarAsync(string userId, string? avatarUrl)
        {
            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            if (profile == null)
            {
                profile = new UserProfile { UserId = userId };
                _context.UserProfiles.Add(profile);
            }

            profile.AvatarUrl = avatarUrl;
            await _context.SaveChangesAsync();
        }

        public async Task<string> VerifyActivationAsync(VerifyActivationDto dto)
        {
            var token = await _context.AccountActivationTokens
                .Include(t => t.User).ThenInclude(u => u.Profile)
                .FirstOrDefaultAsync(t => t.Token == dto.Token && !t.Used);

            if (token == null || token.ExpiresAt < DateTime.UtcNow)
                throw new ApiException("Invalid or expired token");

            if (token.User.Profile == null || token.User.Profile.BirthDate != dto.BirthDate)
                throw new ApiException("Identity verification failed (DOB mismatch)");

            token.Used = true;
            await _context.SaveChangesAsync();

            var tempToken = Guid.NewGuid().ToString();
            await _cache.SetStringAsync($"ACTIVATE_USER_{tempToken}", token.UserId,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });

            return tempToken;
        }

        public async Task CompleteActivationAsync(CompleteActivationDto dto)
        {
            if (dto.NewPassword != dto.ConfirmPassword)
                throw new ApiException("Passwords do not match.");

            var userId = await _cache.GetStringAsync($"ACTIVATE_USER_{dto.TemporaryToken}");
            if (userId == null) throw new ApiException("Session expired");

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) throw new ApiException("User not found");

            var result = await _userManager.AddPasswordAsync(user, dto.NewPassword);
            if (!result.Succeeded) throw new ApiException("Failed to set password");

            user.IsActive = true;
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);

            await _cache.RemoveAsync($"ACTIVATE_USER_{dto.TemporaryToken}");
        }
    }
}