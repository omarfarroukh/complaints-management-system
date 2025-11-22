using Microsoft.AspNetCore.Identity; // From Microsoft.Extensions.Identity.Stores

namespace CMS.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    // Core Identity Properties
    public UserType UserType { get; set; }
    
    public Department? Department { get; set; } 

    public bool IsActive { get; set; } = false; // Default false until activation/verify
    public string? MfaSecret { get; set; }

    // Navigation Properties
    public virtual UserProfile? Profile { get; set; }
    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}