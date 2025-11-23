using CMS.Domain.Common;

namespace CMS.Application.DTOs
{
    public class ComplaintDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string DepartmentId { get; set; } = string.Empty;
        public string CitizenId { get; set; } = string.Empty;
        public string? CitizenName { get; set; }
        public string? AssignedEmployeeId { get; set; }
        public string? AssignedEmployeeName { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime? LastModifiedOn { get; set; }
        public DateTime? AssignedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }

        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string Address { get; set; } = string.Empty;

        public List<ComplaintAttachmentDto> Attachments { get; set; } = new();
    }

    public class ComplaintAttachmentDto
    {
        public Guid Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime UploadedOn { get; set; }
        public long FileSize { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public bool IsScanned { get; set; }
        public string? ScanResult { get; set; }
    }

    public class CreateComplaintDto
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DepartmentId { get; set; } = string.Empty;
        public string Priority { get; set; } = "Low"; // Default

        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string Address { get; set; } = string.Empty;
    }

    public class AssignComplaintDto
    {
        public string EmployeeId { get; set; } = string.Empty;
    }

    public class UpdateComplaintStatusDto
    {
        public string Status { get; set; } = string.Empty;
    }

    public class AddNoteDto
    {
        public string Note { get; set; } = string.Empty;
    }

    public class ComplaintAuditLogDto
    {
        public Guid Id { get; set; }
        public string ChangeSummary { get; set; } = string.Empty;
        public string ChangedByUserId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }
    }
}
