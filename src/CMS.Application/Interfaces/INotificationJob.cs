using CMS.Domain.Common;

namespace CMS.Application.Interfaces
{
    public interface INotificationJob
    {
        Task SendComplaintAssignedNotificationAsync(string employeeId, Guid complaintId, string complaintTitle);
        Task SendComplaintStatusChangedNotificationAsync(string citizenId, Guid complaintId, string complaintTitle, ComplaintStatus newStatus);
        Task SendComplaintCreatedNotificationAsync(string departmentId, string complaintTitle);
        Task SendComplaintAttachmentUploadedNotificationAsync(Guid complaintId, string fileName);
        Task SendComplaintNoteAddedNotificationAsync(Guid complaintId, string note);
    }
}
