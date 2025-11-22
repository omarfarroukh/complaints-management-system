using System.ComponentModel.DataAnnotations;

namespace CMS.Application.DTOs;

public class BlacklistIpDto
{
    [Required] public string IpAddress { get; set; } = string.Empty;
    [Required] public string Reason { get; set; } = string.Empty;
}