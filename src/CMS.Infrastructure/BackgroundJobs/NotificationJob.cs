using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using CMS.Domain.Common;
using CMS.Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace CMS.Infrastructure.BackgroundJobs
{
    public class NotificationJob : INotificationJob
    {
        private readonly INotificationService _notificationService;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ILogger<NotificationJob> _logger;
        private readonly IComplaintService _complaintService;

        public NotificationJob(
            INotificationService notificationService,
            IHubContext<NotificationHub> hubContext,
            ILogger<NotificationJob> logger,
            IComplaintService complaintService)
        {
            _notificationService = notificationService;
            _hubContext = hubContext;
            _logger = logger;
            _complaintService = complaintService;
        }

        public async Task SendComplaintAssignedNotificationAsync(string employeeId, Guid complaintId, string complaintTitle)
        {
            try
            {
                _logger.LogInformation("Sending complaint assigned notification to employee {EmployeeId}", employeeId);

                var complaint = await _complaintService.GetComplaintByIdAsync(complaintId);
                if (complaint == null)
                {
                    _logger.LogWarning("Complaint {ComplaintId} not found, skipping notifications", complaintId);
                    return;
                }

                var employeeNotificationId = await _notificationService.CreateNotificationAsync(
                    employeeId,
                    "New Assignment",
                    $"You have been assigned a new complaint: {complaintTitle}",
                    NotificationType.Info,
                    "Complaint",
                    complaintId);

                var employeeNotification = await _notificationService.GetNotificationByIdAsync(employeeNotificationId, employeeId);
                if (employeeNotification != null)
                {
                    await _hubContext.Clients.User(employeeId).SendAsync("ReceiveNotification", employeeNotification);
                }

                var citizenNotificationId = await _notificationService.CreateNotificationAsync(
                    complaint.CitizenId,
                    "Complaint Assigned",
                    $"Your complaint '{complaintTitle}' has been assigned to an employee.",
                    NotificationType.Success,
                    "Complaint",
                    complaintId);

                var citizenNotification = await _notificationService.GetNotificationByIdAsync(citizenNotificationId, complaint.CitizenId);
                if (citizenNotification != null)
                {
                    await _hubContext.Clients.User(complaint.CitizenId).SendAsync("ReceiveNotification", citizenNotification);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send complaint assigned notification to employee {EmployeeId}", employeeId);
                throw;
            }
        }

        public async Task SendComplaintStatusChangedNotificationAsync(string citizenId, Guid complaintId, string complaintTitle, ComplaintStatus newStatus)
        {
            try
            {
                _logger.LogInformation("Sending complaint status changed notification to citizen {CitizenId}", citizenId);

                var notificationType = newStatus == ComplaintStatus.Resolved
                    ? NotificationType.Success
                    : NotificationType.Info;

                var notificationId = await _notificationService.CreateNotificationAsync(
                    citizenId,
                    "Complaint Status Updated",
                    $"Your complaint '{complaintTitle}' status changed to {newStatus}.",
                    notificationType,
                    "Complaint",
                    complaintId);

                var notification = await _notificationService.GetNotificationByIdAsync(notificationId, citizenId);

                if (notification != null)
                {
                    await _hubContext.Clients.User(citizenId).SendAsync("ReceiveNotification", notification);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send complaint status changed notification to citizen {CitizenId}", citizenId);
                throw;
            }
        }

        public async Task SendComplaintCreatedNotificationAsync(string departmentId, string complaintTitle)
        {
            try
            {
                _logger.LogInformation("Broadcasting complaint created notification to department {DepartmentId}", departmentId);
                await _hubContext.Clients.Group(departmentId).SendAsync("ReceiveDepartmentMessage", $"New complaint created: {complaintTitle}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast complaint created notification to department {DepartmentId}", departmentId);
                throw;
            }
        }

        public async Task SendComplaintAttachmentUploadedNotificationAsync(Guid complaintId, string fileName)
        {
            await _hubContext.Clients.All.SendAsync("ReceiveNotification", $"Attachment {fileName} uploaded to complaint {complaintId}");
        }

        public async Task SendComplaintNoteAddedNotificationAsync(Guid complaintId, string note)
        {
            await _hubContext.Clients.All.SendAsync("ReceiveNotification", $"Note added to complaint {complaintId}: {note}");
        }
    }
}
