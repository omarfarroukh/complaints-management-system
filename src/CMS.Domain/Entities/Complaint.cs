using CMS.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CMS.Domain.Entities
{
    public class Complaint : AuditableEntity
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty;

        public ComplaintStatus Status { get; set; } = ComplaintStatus.Pending;

        public ComplaintPriority Priority { get; set; } = ComplaintPriority.Low;

        public string DepartmentId { get; set; } = string.Empty;

        [Required]
        public string CitizenId { get; set; } = string.Empty;

        [ForeignKey(nameof(CitizenId))]
        public virtual ApplicationUser? Citizen { get; set; }

        public string? AssignedEmployeeId { get; set; }

        [ForeignKey(nameof(AssignedEmployeeId))]
        public virtual ApplicationUser? AssignedEmployee { get; set; }

        public virtual ICollection<ComplaintAttachment> Attachments { get; set; } = new List<ComplaintAttachment>();
        public virtual ICollection<ComplaintAuditLog> AuditLogs { get; set; } = new List<ComplaintAuditLog>();

        // [Column] removed. Precision handled in DbContext
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }

        public string Address { get; set; } = string.Empty;

        public string Metadata { get; set; } = "{}"; 

        public DateTime? AssignedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }

        public string? LockToken { get; set; }
        public DateTime? LockExpiresAt { get; set; }
    }
}