namespace CMS.Domain.Entities
{
    public class LoginAttempt
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? UserId { get; set; } 
        public string IpAddress { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? FailureReason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}