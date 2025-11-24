using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CMS.Domain.Entities
{
    public class ComplaintAuditLog
    {
        [Key]
        public Guid Id { get; set; }

        public Guid ComplaintId { get; set; }

        [ForeignKey(nameof(ComplaintId))]
        public virtual Complaint? Complaint { get; set; }

        [Required]
        public string ChangeSummary { get; set; } = string.Empty;

        [Required]
        public string ChangedByUserId { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public string? OldValues { get; set; }
        public string? NewValues { get; set; }
    }
}