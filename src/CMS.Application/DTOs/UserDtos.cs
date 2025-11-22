using System.ComponentModel.DataAnnotations;
using CMS.Domain.Entities;

namespace CMS.Application.DTOs;

// Used for listing users

public class ProfileInputDto
{
    [Required] public string FirstName { get; set; } = string.Empty;
    [Required] public string LastName { get; set; } = string.Empty;
    public string? NationalId { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? MetadataJson { get; set; }
}


public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Department { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}

// Used for creating Employees/Managers
public class CreateUserDto
{
    [Required][EmailAddress] public string Email { get; set; } = string.Empty;
    public Department? Department { get; set; }

    // Nested Profile Object
    [Required] public ProfileInputDto Profile { get; set; } = new();
}

// Used for Profile Logic
public class UserProfileDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? NationalId { get; set; }
    public string? AvatarUrl { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
}

public class UpdateProfileDto
{
    // Make EVERYTHING nullable. 
    // If it's null, we assume it wasn't sent in the JSON, so we ignore it.
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? NationalId { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
}