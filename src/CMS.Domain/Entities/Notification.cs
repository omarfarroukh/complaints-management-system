using CMS.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

namespace CMS.Domain.Entities
{
    [Index(nameof(UserId))]
    [Index(nameof(IsRead))]
    [Index(nameof(CreatedOn))]
    public class Notification : AuditableEntity
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public virtual ApplicationUser? User { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;

        public NotificationType Type { get; set; } = NotificationType.Info;

        public bool IsRead { get; set; } = false;

        public DateTime? ReadAt { get; set; }

        // Optional: Link to related entity (e.g., Complaint)
        [MaxLength(100)]
        public string? RelatedEntityType { get; set; }

        public Guid? RelatedEntityId { get; set; }
    }
}
