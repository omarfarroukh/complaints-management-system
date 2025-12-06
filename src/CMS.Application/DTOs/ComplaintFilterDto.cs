namespace CMS.Application.DTOs
{
    public class ComplaintFilterDto
    {
        // Existing properties...
        public string? SearchTerm { get; set; }
        public string? Status { get; set; }
        public string? Priority { get; set; }
        public string? DepartmentId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? SortBy { get; set; }
        public bool SortDescending { get; set; }

        public int Skip { get; set; } = 0;
        public int Take { get; set; } = int.MaxValue; 
    }
}