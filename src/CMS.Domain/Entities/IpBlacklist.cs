using Microsoft.EntityFrameworkCore;

namespace CMS.Domain.Entities
{
    [Index(nameof(IpAddress))]
    [Index(nameof(CreatedAt))]
    public class IpBlacklist
    {
        public int Id { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }
}