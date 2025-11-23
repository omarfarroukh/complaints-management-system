namespace CMS.Application.DTOs
{
    public class ComplaintFilterDto
    {
        public string? SearchTerm { get; set; }
        public string? Status { get; set; }
        public string? Priority { get; set; }
        public string? DepartmentId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? SortBy { get; set; } // e.g., "CreatedOn", "Priority"
        public bool SortDescending { get; set; } = true;
    }
}
