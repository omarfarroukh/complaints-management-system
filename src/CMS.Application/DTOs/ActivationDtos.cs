using System.ComponentModel.DataAnnotations;

namespace CMS.Application.DTOs;

public class VerifyActivationDto
{
    [Required] public string Token { get; set; } = string.Empty;
    [Required] public DateOnly BirthDate { get; set; }
}

public class CompleteActivationDto
{
    [Required] public string TemporaryToken { get; set; } = string.Empty;
    [Required] public string NewPassword { get; set; } = string.Empty;

    [Required]
    [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}