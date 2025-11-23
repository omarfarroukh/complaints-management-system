namespace CMS.Application.DTOs
{
    public class PatchComplaintDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Priority { get; set; }
        public string? DepartmentId { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string? Address { get; set; }
        public string? Metadata { get; set; } // JSON string
    }
}
