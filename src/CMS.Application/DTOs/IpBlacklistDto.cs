namespace CMS.Application.DTOs;

public class IpBlacklistDto
{
    public int Id { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
