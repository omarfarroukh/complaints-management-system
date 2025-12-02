using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CMS.Application.DTOs;
using CMS.Application.Exceptions;
using CMS.Application.Interfaces;
using CMS.Domain.Entities;
using CMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Hangfire;
using OtpNet;
using Microsoft.Extensions.Caching.Distributed;

namespace CMS.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;
    private readonly AppDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly IRateLimitService _rateLimitService;

    public AuthService(UserManager<ApplicationUser> userManager,
                        SignInManager<ApplicationUser> signInManager,
                       IConfiguration configuration,
                       IEmailService emailService,
                       AppDbContext context,
                       IDistributedCache cache,
                       IRateLimitService rateLimitService)

    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
        _emailService = emailService;
        _context = context;
        _cache = cache;
        _rateLimitService = rateLimitService;
    }

    public async Task<string> RegisterAsync(RegisterDto dto)
    {
        // [Existing Register Logic - No Changes needed here]
        if (dto.Password != dto.ConfirmPassword)
            throw new ApiException("Passwords do not match.");

        var user = new ApplicationUser
        {
            UserName = dto.Email,
            Email = dto.Email,
            UserType = dto.UserType,
            IsActive = true
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            throw new ApiException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

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
        await _context.SaveChangesAsync(); 

        var otp = new Random().Next(100000, 999999).ToString();

        await _cache.SetStringAsync(
            $"EMAIL_CONFIRM_{user.Id}",
            otp,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) }
        );

        BackgroundJob.Enqueue<IEmailService>(
            x => x.SendEmailAsync(user.Email!, "Confirm Email", $"Your Verification Code is: <b>{otp}</b>"));

        return "User registered successfully. Please check your email for the verification code.";
    }
    public async Task<bool> IsEmailUsedAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        return user != null;
    }
    public async Task<AuthResponseDto> LoginAsync(LoginDto dto, string ipAddress)
    {

        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null)
        {
            BackgroundJob.Enqueue<ISecurityService>(x => x.LogLoginAttemptAsync(dto.Email, ipAddress, null, false, "User not found"));
            throw new ApiException("Invalid email or password.");
        }
        
        var result = await _signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);
        
        if (result.IsLockedOut)
        {
            BackgroundJob.Enqueue<ISecurityService>(x => x.LogLoginAttemptAsync(dto.Email, ipAddress, user.Id, false, "Account locked"));

            string cacheKey = $"LOCKOUT_ALERT_SENT_{user.Id}";
            var emailAlreadySent = await _cache.GetStringAsync(cacheKey);
            if (string.IsNullOrEmpty(emailAlreadySent))
            {
                var time = DateTime.UtcNow.ToString("g");
                var subject = "Security Alert: Account Locked";
                var body = $@"
                    <h3>Your Account has been locked</h3>
                    <p>Multiple failed login attempts detected.</p>
                    <ul>
                        <li><b>Time:</b> {time} (UTC)</li>
                        <li><b>IP Address:</b> {ipAddress}</li>
                    </ul>
                    <p>Please wait 5 minutes before trying again.</p>";
                
                BackgroundJob.Enqueue<IEmailService>(x => x.SendEmailAsync(user.Email!, subject, body));
                
                await _cache.SetStringAsync(
                    cacheKey,
                    "true",
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) }
                );
            }
            throw new ApiException("Your account is locked due to multiple failed attempts. Please try again later.");
        }

        if (result.IsNotAllowed)
        {
            BackgroundJob.Enqueue<ISecurityService>(x => x.LogLoginAttemptAsync(dto.Email, ipAddress, user.Id, false, "Email not confirmed"));
            throw new ApiException("Please confirm your email before logging in.");
        }

        if (result.RequiresTwoFactor || (result.Succeeded && user.TwoFactorEnabled))
        {
            if (string.IsNullOrEmpty(dto.MfaCode) || dto.MfaCode == "string")
            {
                throw new ApiException("MFA_REQUIRED");
            }
            if (string.IsNullOrEmpty(user.MfaSecret))
            {
                throw new ApiException("MFA is enabled but not configured properly.");
            }
            var totp = new Totp(Base32Encoding.ToBytes(user.MfaSecret));
            
            if (!totp.VerifyTotp(dto.MfaCode, out _, new VerificationWindow(2, 2)))
            {
                BackgroundJob.Enqueue<ISecurityService>(x => x.LogLoginAttemptAsync(dto.Email, ipAddress, user.Id, false, "Invalid MFA code"));
                throw new ApiException("Invalid MFA Code");
            }
            
            BackgroundJob.Enqueue<ISecurityService>(x => x.LogLoginAttemptAsync(dto.Email, ipAddress, user.Id, true, null));
            return await GenerateTokenPairAsync(user);
        }

        if (result.Succeeded)
        {
            BackgroundJob.Enqueue<ISecurityService>(x => x.LogLoginAttemptAsync(dto.Email, ipAddress, user.Id, true, null));
            return await GenerateTokenPairAsync(user);
        }

        BackgroundJob.Enqueue<ISecurityService>(x => x.LogLoginAttemptAsync(dto.Email, ipAddress, user.Id, false, "Invalid password"));

        var failureCount = await _rateLimitService.IncrementLoginFailureAsync(ipAddress, dto.Email);

        if (failureCount >= 20)
        {
            await _rateLimitService.AutoBlacklistAsync(ipAddress,
                $"Auto-blacklisted: {failureCount} failed login attempts");
            throw new ApiException("Your IP has been blocked due to suspicious activity.");
        }

        throw new ApiException("Invalid email or password.");
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(RefreshTokenDto dto)
    {
        var hashedToken = HashToken(dto.RefreshToken);
        var storedToken = await _context.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(x => x.Token == hashedToken);

        if (storedToken == null) throw new ApiException("Invalid Refresh Token");
        if (storedToken.IsRevoked) throw new ApiException("Token has been revoked");
        if (storedToken.ExpiryDate <= DateTime.UtcNow) throw new ApiException("Refresh Token has expired");

        storedToken.IsRevoked = true;
        _context.RefreshTokens.Update(storedToken);
        await _context.SaveChangesAsync();

        return await GenerateTokenPairAsync(storedToken.User);
    }

    public async Task ConfirmEmailAsync(ConfirmEmailDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null) throw new ApiException("User not found");
        if (user.EmailConfirmed) return; 

        var cachedOtp = await _cache.GetStringAsync($"EMAIL_CONFIRM_{user.Id}");

        if (cachedOtp == null) throw new ApiException("Verification code expired. Please request a new one.");
        if (cachedOtp != dto.Token) throw new ApiException("Invalid verification code.");

        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);
        await _cache.RemoveAsync($"EMAIL_CONFIRM_{user.Id}");
    }

    public async Task ResendConfirmationEmailAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null || user.EmailConfirmed) return;

        var otp = new Random().Next(100000, 999999).ToString();
        await _cache.SetStringAsync(
            $"EMAIL_CONFIRM_{user.Id}",
            otp,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) }
        );

        BackgroundJob.Enqueue<IEmailService>(
            x => x.SendEmailAsync(user.Email!, "Confirm Email", $"Your Verification Code is: <b>{otp}</b>"));
    }

    public async Task ForgotPasswordAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null) return; 

        var otp = new Random().Next(100000, 999999).ToString();
        await _cache.SetStringAsync(
            $"PASSWORD_RESET_{user.Id}",
            otp,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) }
        );

        BackgroundJob.Enqueue<IEmailService>(
            x => x.SendEmailAsync(user.Email!, "Reset Password", $"Your Password Reset Code is: <b>{otp}</b>"));
    }

    public async Task ResetPasswordAsync(ResetPasswordDto dto)
    {
        if (dto.NewPassword != dto.ConfirmPassword)
            throw new ApiException("Passwords do not match.");

        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null) throw new ApiException("Invalid Request");

        var cachedOtp = await _cache.GetStringAsync($"PASSWORD_RESET_{user.Id}");
        if (cachedOtp == null) throw new ApiException("Reset code expired. Please request a new one.");
        if (cachedOtp != dto.Token) throw new ApiException("Invalid reset code.");

        if (await _userManager.HasPasswordAsync(user))
        {
            await _userManager.RemovePasswordAsync(user);
        }

        var result = await _userManager.AddPasswordAsync(user, dto.NewPassword);
        if (!result.Succeeded)
            throw new ApiException(string.Join(", ", result.Errors.Select(e => e.Description)));

        await _cache.RemoveAsync($"PASSWORD_RESET_{user.Id}");
        await _userManager.UpdateSecurityStampAsync(user);
    }

    public async Task ChangePasswordAsync(string userId, ChangePasswordDto dto)
    {
        if (dto.NewPassword != dto.ConfirmPassword)
            throw new ApiException("Passwords do not match.");

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) throw new ApiException("User not found");

        var result = await _userManager.ChangePasswordAsync(user, dto.OldPassword, dto.NewPassword);
        if (!result.Succeeded) throw new ApiException("Password change failed");
    }

    public async Task CleanupTokensAsync(string userId)
    {
        try
        {
            var junkTokens = await _context.RefreshTokens
                .Where(r => r.UserId == userId && (r.IsRevoked || r.ExpiryDate <= DateTime.UtcNow))
                .ToListAsync();

            if (junkTokens.Any())
            {
                _context.RefreshTokens.RemoveRange(junkTokens);
            }

            var activeTokens = await _context.RefreshTokens
                .Where(r => r.UserId == userId && !r.IsRevoked && r.ExpiryDate > DateTime.UtcNow)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync();

            if (activeTokens.Count >= 5)
            {
                var tokensToRemove = activeTokens.Take(activeTokens.Count - 4).ToList();
                _context.RefreshTokens.RemoveRange(tokensToRemove);
            }
            await _context.SaveChangesAsync();
        }
        catch { }
    }

    public async Task<MfaSetupResponseDto> GenerateMfaSetupAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) throw new ApiException("User not found");

        var key = KeyGeneration.GenerateRandomKey(20);
        var base32String = Base32Encoding.ToString(key);

        user.MfaSecret = base32String;
        await _userManager.UpdateAsync(user);

        var uri = $"otpauth://totp/CMS:{user.Email}?secret={base32String}&issuer=CMS";
        return new MfaSetupResponseDto { Secret = base32String, QrCodeUri = uri };
    }

    public async Task EnableMfaAsync(string userId, string code)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) throw new ApiException("User not found");
        if (string.IsNullOrEmpty(user.MfaSecret)) throw new ApiException("MFA setup not initiated");

        var totp = new Totp(Base32Encoding.ToBytes(user.MfaSecret));
        if (!totp.VerifyTotp(code, out _, new VerificationWindow(2, 2)))
        {
            throw new ApiException("Invalid Code");
        }

        user.TwoFactorEnabled = true;
        await _userManager.UpdateAsync(user);
    }

    public async Task DisableMfaAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null)
        {
            user.TwoFactorEnabled = false;
            user.MfaSecret = null;
            await _userManager.UpdateAsync(user);
        }
    }
    public async Task<bool> GetMfaStatusAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        return user != null && user.TwoFactorEnabled;
    }
    
    private async Task<AuthResponseDto> GenerateTokenPairAsync(ApplicationUser user)
    {
        BackgroundJob.Enqueue<IAuthService>(x => x.CleanupTokensAsync(user.Id));

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(ClaimTypes.Role, user.UserType.ToString()),
        };
        if (user.Department.HasValue)
        {
            claims.Add(new Claim("DepartmentId", ((int)user.Department.Value).ToString()));
        }
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds
        );

        var rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshTokenEntity = new RefreshToken
        {
            Token = HashToken(rawRefreshToken),
            UserId = user.Id,
            ExpiryDate = DateTime.UtcNow.AddDays(7)
        };

        _context.RefreshTokens.Add(refreshTokenEntity);
        _context.Entry(user).State = EntityState.Unchanged;
        await _context.SaveChangesAsync();

        return new AuthResponseDto
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            RefreshToken = rawRefreshToken,
            Email = user.Email!,
            UserId = user.Id
        };
    }
    private string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    // REMOVED: GetClientIpAddress method entirely.
}