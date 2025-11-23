using CMS.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace CMS.Domain.Entities
{
    [Index(nameof(Status))]
    [Index(nameof(Priority))]
    [Index(nameof(CreatedOn))]
    [Index(nameof(DepartmentId))]
    [Index(nameof(CitizenId))]
    [Index(nameof(AssignedEmployeeId))]
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

        // The department responsible for this complaint
        public string DepartmentId { get; set; } = string.Empty;

        // The citizen who created the complaint
        [Required]
        public string CitizenId { get; set; } = string.Empty;

        [ForeignKey(nameof(CitizenId))]
        public virtual ApplicationUser? Citizen { get; set; }

        // The employee assigned to resolve the complaint
        public string? AssignedEmployeeId { get; set; }

        [ForeignKey(nameof(AssignedEmployeeId))]
        public virtual ApplicationUser? AssignedEmployee { get; set; }

        public virtual ICollection<ComplaintAttachment> Attachments { get; set; } = new List<ComplaintAttachment>();
        public virtual ICollection<ComplaintAuditLog> AuditLogs { get; set; } = new List<ComplaintAuditLog>();

        // Location Data
        [Column(TypeName = "decimal(10, 7)")]
        public decimal? Latitude { get; set; }

        [Column(TypeName = "decimal(10, 7)")]
        public decimal? Longitude { get; set; }

        public string Address { get; set; } = string.Empty;

        // Metadata
        public string Metadata { get; set; } = "{}"; // JSON string

        // Timestamps
        public DateTime? AssignedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }

        // Redis Lock Fields (persisted for recovery/audit if needed, though primarily in Redis)
        public string? LockToken { get; set; }
        public DateTime? LockExpiresAt { get; set; }
    }
}
