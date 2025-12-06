using CMS.Application.DTOs;
using CMS.Domain.Common;
using CMS.Domain.Entities;
namespace CMS.Application.Interfaces
{
    public interface IComplaintService
    {
        Task<ComplaintDto> CreateComplaintAsync(CreateComplaintDto dto, string citizenId);
        Task<ComplaintDto?> GetComplaintByIdAsync(Guid id);
        Task<PagedResult<ComplaintDto>> GetComplaintsForUserAsync(string userId, string role, ComplaintFilterDto filter);        
        Task AssignComplaintAsync(Guid complaintId, string employeeId, string managerId);
        Task UpdateComplaintStatusAsync(Guid complaintId, ComplaintStatus status, string userId);
        Task<Guid> AddAttachmentAsync(Guid complaintId, string filePath, string fileName, long fileSize, string mimeType, string userId);
        Task AddNoteAsync(Guid complaintId, string note, string userId);
        Task<List<ComplaintAuditLogDto>> GetComplaintVersionsAsync(Guid complaintId);
        Task<ComplaintDto> UpdateComplaintDetailsAsync(Guid complaintId, PatchComplaintDto dto, string userId, string role);
    }
}
