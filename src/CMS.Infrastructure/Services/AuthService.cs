using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CMS.Application.DTOs;
using CMS.Application.Exceptions; // Uses our custom exception
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

using Microsoft.AspNetCore.Http;
public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;
    private readonly AppDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly IHttpContextAccessor _httpContextAccessor; // <--- Add Field
    private readonly IRateLimitService _rateLimitService;


    public AuthService(UserManager<ApplicationUser> userManager,
                        SignInManager<ApplicationUser> signInManager,
                       IConfiguration configuration,
                       IEmailService emailService,
                       AppDbContext context,
                       IDistributedCache cache,
                        IHttpContextAccessor httpContextAccessor, // <--- Inject Here
                        IRateLimitService rateLimitService)

    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
        _emailService = emailService;
        _context = context;
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
        _rateLimitService = rateLimitService;

    }

    public async Task<string> RegisterAsync(RegisterDto dto)
    {
        // REMOVED: await _context.Database.BeginTransactionAsync(); 
        // The Controller's [Transactional] attribute now holds the DB lock.

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
            // REMOVED: await tx.RollbackAsync(); 
            // Just throw the exception. The Attribute will catch it and Rollback automatically.
            throw new ApiException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        // Create Profile
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
        await _context.SaveChangesAsync(); // This is still needed to generate Insert statements

        // Generate 6-Digit OTP
        var otp = new Random().Next(100000, 999999).ToString();

        await _cache.SetStringAsync(
            $"EMAIL_CONFIRM_{user.Id}",
            otp,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) }
        );

        BackgroundJob.Enqueue<IEmailService>(
            x => x.SendEmailAsync(user.Email!, "Confirm Email", $"Your Verification Code is: <b>{otp}</b>"));

        // REMOVED: await tx.CommitAsync(); 
        // The Attribute will Commit if no exception was thrown.

        return "User registered successfully. Please check your email for the verification code.";
    }
    public async Task<AuthResponseDto> LoginAsync(LoginDto dto)
    {
        var ip = GetClientIpAddress();

        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null)
        {
            // Log failed attempt - user not found
            BackgroundJob.Enqueue<ISecurityService>(x => x.LogLoginAttemptAsync(dto.Email, ip, null, false, "User not found"));
            throw new ApiException("Invalid email or password.");
        }
        // Check Password
        var result = await _signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);
        // 1. Handle Lockout first
        if (result.IsLockedOut)
        {
            // Log lockout attempt  
            BackgroundJob.Enqueue<ISecurityService>(x => x.LogLoginAttemptAsync(dto.Email, ip, user.Id, false, "Account locked"));

            // A. Check Redis: Did we already send an email in the last 10 mins?
            string cacheKey = $"LOCKOUT_ALERT_SENT_{user.Id}";
            var emailAlreadySent = await _cache.GetStringAsync(cacheKey);
            if (string.IsNullOrEmpty(emailAlreadySent))
            {
                // B. Prepare Data
                var time = DateTime.UtcNow.ToString("g");
                var subject = "Security Alert: Account Locked";
                var body = $@"
                    <h3>Your Account has been locked</h3>
                    <p>Multiple failed login attempts detected.</p>
                    <ul>
                        <li><b>Time:</b> {time} (UTC)</li>
                        <li><b>IP Address:</b> {ip}</li>
                    </ul>
                    <p>Please wait 5 minutes before trying again.</p>";
                // C. Send Email (Fire-and-Forget)
                BackgroundJob.Enqueue<IEmailService>(x => x.SendEmailAsync(user.Email!, subject, body));
                // D. Mark as Sent in Redis for 10 minutes
                await _cache.SetStringAsync(
                    cacheKey,
                    "true",
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) }
                );
            }
            throw new ApiException("Your account is locked due to multiple failed attempts. Please try again later.");
        }
        // 2. Handle Not Allowed (e.g. Email not confirmed)
        if (result.IsNotAllowed)
        {
            BackgroundJob.Enqueue<ISecurityService>(x => x.LogLoginAttemptAsync(dto.Email, ip, user.Id, false, "Email not confirmed"));
            throw new ApiException("Please confirm your email before logging in.");
        }
        // 3. CHECK FOR MFA
        if (result.RequiresTwoFactor || (result.Succeeded && user.TwoFactorEnabled))
        {
            // Check if code is missing or is the default Swagger "string"
            if (string.IsNullOrEmpty(dto.MfaCode) || dto.MfaCode == "string")
            {
                throw new ApiException("MFA_REQUIRED");
            }
            if (string.IsNullOrEmpty(user.MfaSecret))
            {
                throw new ApiException("MFA is enabled but not configured properly.");
            }
            var totp = new Totp(Base32Encoding.ToBytes(user.MfaSecret));
            // Verify the code
            if (!totp.VerifyTotp(dto.MfaCode, out _, new VerificationWindow(2, 2)))
            {
                BackgroundJob.Enqueue<ISecurityService>(x => x.LogLoginAttemptAsync(dto.Email, ip, user.Id, false, "Invalid MFA code"));
                throw new ApiException("Invalid MFA Code");
            }
            // MFA Verified successfully -> Generate Token
            BackgroundJob.Enqueue<ISecurityService>(x => x.LogLoginAttemptAsync(dto.Email, ip, user.Id, true, null));
            return await GenerateTokenPairAsync(user);
        }
        // 4. Normal Login (Password correct, 2FA disabled)
        if (result.Succeeded)
        {
            BackgroundJob.Enqueue<ISecurityService>(x => x.LogLoginAttemptAsync(dto.Email, ip, user.Id, true, null));
            return await GenerateTokenPairAsync(user);
        }
        // 5. Password Wrong
        BackgroundJob.Enqueue<ISecurityService>(x => x.LogLoginAttemptAsync(dto.Email, ip, user.Id, false, "Invalid password"));

        // Increment failure counter and check threshold
        var failureCount = await _rateLimitService.IncrementLoginFailureAsync(ip, dto.Email);

        if (failureCount >= 20)
        {
            await _rateLimitService.AutoBlacklistAsync(ip,
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
            .FirstOrDefaultAsync(x => x.Token == dto.RefreshToken);

        if (storedToken == null) throw new ApiException("Invalid Refresh Token");
        if (storedToken.IsRevoked) throw new ApiException("Token has been revoked");
        if (storedToken.ExpiryDate <= DateTime.UtcNow) throw new ApiException("Refresh Token has expired");

        // Rotate Token
        storedToken.IsRevoked = true;
        _context.RefreshTokens.Update(storedToken);
        await _context.SaveChangesAsync();

        return await GenerateTokenPairAsync(storedToken.User);
    }

    public async Task ConfirmEmailAsync(ConfirmEmailDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null) throw new ApiException("User not found");

        if (user.EmailConfirmed) return; // Already done

        // 1. Get OTP from Redis
        var cachedOtp = await _cache.GetStringAsync($"EMAIL_CONFIRM_{user.Id}");

        // 2. Validate
        if (cachedOtp == null)
            throw new ApiException("Verification code expired. Please request a new one.");

        if (cachedOtp != dto.Token) // dto.Token is now the 6-digit code
            throw new ApiException("Invalid verification code.");

        // 3. Manually Confirm Email
        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);

        // 4. Cleanup
        await _cache.RemoveAsync($"EMAIL_CONFIRM_{user.Id}");
    }

    public async Task ResendConfirmationEmailAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null || user.EmailConfirmed) return;

        // Generate new OTP
        var otp = new Random().Next(100000, 999999).ToString();

        // Overwrite existing Redis key
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
        if (user == null) return; // Silently fail for security

        // 1. Generate 6-digit OTP
        var otp = new Random().Next(100000, 999999).ToString();

        // 2. Store in Redis for 15 minutes
        // Key: "PASSWORD_RESET_userId"
        await _cache.SetStringAsync(
            $"PASSWORD_RESET_{user.Id}",
            otp,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) }
        );

        // 3. Send Email
        BackgroundJob.Enqueue<IEmailService>(
            x => x.SendEmailAsync(user.Email!, "Reset Password", $"Your Password Reset Code is: <b>{otp}</b>"));
    }

    public async Task ResetPasswordAsync(ResetPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null) throw new ApiException("Invalid Request");

        // 1. Verify the 6-Digit OTP from Redis
        var cachedOtp = await _cache.GetStringAsync($"PASSWORD_RESET_{user.Id}");

        if (cachedOtp == null)
            throw new ApiException("Reset code expired. Please request a new one.");

        if (cachedOtp != dto.Token)
            throw new ApiException("Invalid reset code.");

        // 2. Remove existing password (if any)
        if (await _userManager.HasPasswordAsync(user))
        {
            await _userManager.RemovePasswordAsync(user);
        }

        // 3. Add the new password
        var result = await _userManager.AddPasswordAsync(user, dto.NewPassword);

        if (!result.Succeeded)
            throw new ApiException(string.Join(", ", result.Errors.Select(e => e.Description)));

        // 4. Cleanup
        await _cache.RemoveAsync($"PASSWORD_RESET_{user.Id}");

        // 5. Security: Invalidate other sessions (Force logout elsewhere)
        await _userManager.UpdateSecurityStampAsync(user);
    }

    public async Task ChangePasswordAsync(string userId, ChangePasswordDto dto)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) throw new ApiException("User not found");

        var result = await _userManager.ChangePasswordAsync(user, dto.OldPassword, dto.NewPassword);
        if (!result.Succeeded) throw new ApiException("Password change failed");
    }

    public async Task CleanupTokensAsync(string userId)
    {
        try
        {
            // 1. Remove Revoked/Expired
            var junkTokens = await _context.RefreshTokens
                .Where(r => r.UserId == userId && (r.IsRevoked || r.ExpiryDate <= DateTime.UtcNow))
                .ToListAsync();

            if (junkTokens.Any())
            {
                _context.RefreshTokens.RemoveRange(junkTokens);
            }

            // 2. Device Limiting (Max 5)
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
        catch (DbUpdateConcurrencyException)
        {
            return;
        }
        catch (Exception)
        {
            return;
        }
    }

    public async Task<MfaSetupResponseDto> GenerateMfaSetupAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) throw new ApiException("User not found");

        // 1. Generate a random secret key
        var key = KeyGeneration.GenerateRandomKey(20);
        var base32String = Base32Encoding.ToString(key);

        // 2. Store secret temporarily (or permanently, waiting for verify)
        user.MfaSecret = base32String;
        await _userManager.UpdateAsync(user);

        // 3. Generate otpauth URL for QR Code
        var uri = $"otpauth://totp/CMS:{user.Email}?secret={base32String}&issuer=CMS";

        return new MfaSetupResponseDto { Secret = base32String, QrCodeUri = uri };
    }

    public async Task EnableMfaAsync(string userId, string code)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) throw new ApiException("User not found");
        if (string.IsNullOrEmpty(user.MfaSecret)) throw new ApiException("MFA setup not initiated");

        // Verify Code
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

    // --- Helper Methods ---

    private async Task<AuthResponseDto> GenerateTokenPairAsync(ApplicationUser user)
    {
        // --- REMOVED ALL CLEANUP CODE FROM HERE ---

        // 1. Fire-and-Forget the cleanup job
        // This moves the locking risk to a background thread that retries automatically if it fails!
        BackgroundJob.Enqueue<IAuthService>(x => x.CleanupTokensAsync(user.Id));

        // 2. Generate JWT (Standard Logic)
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

        // 3. Generate Refresh Token
        var rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshTokenEntity = new RefreshToken
        {
            Token = HashToken(rawRefreshToken), // Store Hash
            UserId = user.Id,
            ExpiryDate = DateTime.UtcNow.AddDays(7)
        };

        _context.RefreshTokens.Add(refreshTokenEntity);

        // 4. Prevent User Table Lock
        _context.Entry(user).State = EntityState.Unchanged;

        await _context.SaveChangesAsync();

        return new AuthResponseDto
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            RefreshToken = rawRefreshToken, // Return Raw Token to user
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

    private string GetClientIpAddress()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null) return "Unknown";

        // Check if behind a proxy (Nginx/Cloudflare)
        if (context.Request.Headers.ContainsKey("X-Forwarded-For"))
            return context.Request.Headers["X-Forwarded-For"].ToString();

        return context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "Unknown";
    }
}