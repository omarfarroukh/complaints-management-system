using System.ComponentModel.DataAnnotations;

namespace CMS.Application.DTOs;

public class VerifyActivationDto
{
    [Required] public string Token { get; set; } = string.Empty;
    [Required] public DateOnly BirthDate { get; set; } // <--- Changed
}

public class CompleteActivationDto
{
    [Required] public string TemporaryToken { get; set; } = string.Empty;
    [Required] public string NewPassword { get; set; } = string.Empty;
}