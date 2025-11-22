namespace CMS.Application.DTOs;

public class UserFilterDto
{
    public string? SearchTerm { get; set; }
    public string? Role { get; set; }
    public string? Department { get; set; }
    public bool? IsActive { get; set; }
    public string? SortBy { get; set; } // e.g. "Email", "Name", "Role"
    public bool IsDescending { get; set; }
}
