using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CMS.Domain.Entities
{
    public class ComplaintAttachment
    {
        [Key]
        public Guid Id { get; set; }

        public Guid ComplaintId { get; set; }

        [ForeignKey(nameof(ComplaintId))]
        public virtual Complaint? Complaint { get; set; }

        [Required]
        public string FilePath { get; set; } = string.Empty;

        [Required]
        public string FileName { get; set; } = string.Empty;

        [Required]
        public string UploadedByUserId { get; set; } = string.Empty;

        public DateTime UploadedOn { get; set; } = DateTime.UtcNow;
    }
}
