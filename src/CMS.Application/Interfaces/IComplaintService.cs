using CMS.Application.DTOs;
using CMS.Domain.Common;

namespace CMS.Application.Interfaces
{
    public interface IComplaintService
    {
        Task<ComplaintDto> CreateComplaintAsync(string title, string description, string departmentId, string citizenId);
        Task<ComplaintDto?> GetComplaintByIdAsync(Guid id);
        Task<List<ComplaintDto>> GetComplaintsForUserAsync(string userId, string role, string? departmentId = null);
        Task AssignComplaintAsync(Guid complaintId, string employeeId, string managerId);
        Task UpdateComplaintStatusAsync(Guid complaintId, ComplaintStatus status, string userId);
        Task AddAttachmentAsync(Guid complaintId, string filePath, string fileName, string userId);
    }
}
