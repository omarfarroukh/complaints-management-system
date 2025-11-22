namespace CMS.Application.DTOs
{
    public class ComplaintLockDto
    {
        public Guid ComplaintId { get; set; }
        public string LockedBy { get; set; } = string.Empty;
        public DateTime LockedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool Success { get; set; }
        public string? Message { get; set; }
    }
}
