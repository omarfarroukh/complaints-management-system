using CMS.Application.DTOs;

namespace CMS.Application.Interfaces;

public interface IAuthService
{
    Task<string> RegisterAsync(RegisterDto dto);
    Task<AuthResponseDto> LoginAsync(LoginDto dto);
    // New methods
    Task<AuthResponseDto> RefreshTokenAsync(RefreshTokenDto dto);
    Task ConfirmEmailAsync(ConfirmEmailDto dto);
    Task ResendConfirmationEmailAsync(string email);
    Task ForgotPasswordAsync(string email);
    Task ResetPasswordAsync(ResetPasswordDto dto);
    Task ChangePasswordAsync(string userId,ChangePasswordDto dto);

    Task CleanupTokensAsync(string userId); // <--- Add this
    Task<MfaSetupResponseDto> GenerateMfaSetupAsync(string userId);
    Task EnableMfaAsync(string userId, string code);
    Task DisableMfaAsync(string userId);
    
}