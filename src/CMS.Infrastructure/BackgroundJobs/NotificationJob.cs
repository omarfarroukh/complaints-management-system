using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using CMS.Domain.Common;
using CMS.Domain.Entities;
using CMS.Infrastructure.Hubs;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CMS.Infrastructure.BackgroundJobs
{
    public class NotificationJob : INotificationJob
    {
        private readonly INotificationService _notificationService;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ILogger<NotificationJob> _logger;
        private readonly IComplaintService _complaintService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IPushNotificationService _pushNotificationService;

        public NotificationJob(
            INotificationService notificationService,
            IHubContext<NotificationHub> hubContext,
            ILogger<NotificationJob> logger,
            IComplaintService complaintService,
            UserManager<ApplicationUser> userManager,
            IPushNotificationService pushNotificationService)
        {
            _notificationService = notificationService;
            _hubContext = hubContext;
            _logger = logger;
            _complaintService = complaintService;
            _userManager = userManager;
            _pushNotificationService = pushNotificationService;
        }

        // --- 1. ASSIGNED ---
        public async Task SendComplaintAssignedNotificationAsync(string employeeId, Guid complaintId, string complaintTitle)
        {
            try
            {
                var complaint = await _complaintService.GetComplaintByIdAsync(complaintId);
                if (complaint == null) return;

                // Notify Employee
                await CreateAndSendAsync(
                    employeeId,
                    "New Assignment",
                    $"You have been assigned a new complaint: {complaintTitle}",
                    NotificationType.Info,
                    "Complaint",
                    complaintId
                );

                // Notify Citizen
                await CreateAndSendAsync(
                    complaint.CitizenId,
                    "Complaint Assigned",
                    $"Your complaint '{complaintTitle}' has been assigned to an employee.",
                    NotificationType.Success,
                    "Complaint",
                    complaintId
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send assignment notification");
            }
        }

        // --- 2. STATUS CHANGE ---
        public async Task SendComplaintStatusChangedNotificationAsync(string citizenId, Guid complaintId, string complaintTitle, ComplaintStatus newStatus)
        {
            try
            {
                var type = newStatus == ComplaintStatus.Resolved ? NotificationType.Success : NotificationType.Info;

                await CreateAndSendAsync(
                    citizenId,
                    "Status Update",
                    $"Your complaint '{complaintTitle}' is now {newStatus}.",
                    type,
                    "Complaint",
                    complaintId
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send status notification");
            }
        }

        // --- 3. DEPARTMENT CREATION ---
        public async Task SendComplaintCreatedNotificationAsync(string departmentId, string complaintTitle)
        {
            try
            {
                if (!Enum.TryParse<Department>(departmentId, out var targetDepartment))
                {
                    _logger.LogWarning("Invalid Department identifier received: {Id}", departmentId);
                    return;
                }

                _logger.LogInformation("Broadcasting complaint created notification to department {Department}", targetDepartment);

                var usersToNotify = await _userManager.Users
                    .Where(u => u.Department == targetDepartment &&
                                (u.UserType == UserType.DepartmentManager || u.UserType == UserType.Employee))
                    .ToListAsync();

                foreach (var user in usersToNotify)
                {
                    await CreateAndSendAsync(
                        user.Id,
                        "New Department Complaint",
                        $"A new complaint '{complaintTitle}' has been submitted to your department.",
                        NotificationType.Warning,
                        "Complaint",
                        null 
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send department notification");
            }
        }

        // --- 4. ATTACHMENT UPLOADED ---
        public async Task SendComplaintAttachmentUploadedNotificationAsync(Guid complaintId, string fileName, string uploadedByUserId)
        {
            try
            {
                var complaint = await _complaintService.GetComplaintByIdAsync(complaintId);
                if (complaint == null) return;

                var uploader = await _userManager.FindByIdAsync(uploadedByUserId);
                if (uploader == null) return;

                string uploaderName = uploader.UserName ?? "A user";
                string title = "New Attachment";
                string message = $"{uploaderName} uploaded '{fileName}' to complaint '{complaint.Title}'";

                // Notify Citizen
                if (complaint.CitizenId != uploadedByUserId)
                {
                    await CreateAndSendAsync(
                        complaint.CitizenId, title, message, NotificationType.Info, "Complaint", complaintId
                    );
                }

                // Notify Employee
                if (!string.IsNullOrEmpty(complaint.AssignedEmployeeId) && complaint.AssignedEmployeeId != uploadedByUserId)
                {
                    await CreateAndSendAsync(
                        complaint.AssignedEmployeeId, title, message, NotificationType.Info, "Complaint", complaintId
                    );
                }

                // Notify Department Managers
                if (uploader.UserType != UserType.DepartmentManager)
                {
                    if (Enum.TryParse<Department>(complaint.DepartmentId, out var deptEnum))
                    {
                        var managers = await _userManager.Users
                            .Where(u => u.Department == deptEnum && u.UserType == UserType.DepartmentManager && u.Id != uploadedByUserId)
                            .ToListAsync();

                        foreach (var manager in managers)
                        {
                            await CreateAndSendAsync(
                                manager.Id, title, message, NotificationType.Info, "Complaint", complaintId
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send attachment notification");
            }
        }

        // --- 5. NOTES ---
        public async Task SendComplaintNoteAddedNotificationAsync(Guid complaintId, string note)
        {
            try
            {
                var complaint = await _complaintService.GetComplaintByIdAsync(complaintId);
                if (complaint == null) return;

                string shortNote = note.Length > 30 ? note.Substring(0, 30) + "..." : note;

                // Notify Employee
                if (!string.IsNullOrEmpty(complaint.AssignedEmployeeId))
                {
                    await CreateAndSendAsync(
                        complaint.AssignedEmployeeId,
                        "New Comment",
                        $"New comment on complaint: {shortNote}",
                        NotificationType.Info,
                        "Complaint",
                        complaintId
                    );
                }

                // Notify Citizen
                await CreateAndSendAsync(
                    complaint.CitizenId,
                    "New Response",
                    $"Update on your complaint: {shortNote}",
                    NotificationType.Info,
                    "Complaint",
                    complaintId
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send note notification");
            }
        }

        // --- 6. COMPLAINT DETAILS UPDATED ---
        public async Task SendComplaintUpdatedNotificationAsync(Guid complaintId, string complaintTitle, string actorId)
        {
            try
            {
                var complaint = await _complaintService.GetComplaintByIdAsync(complaintId);
                if (complaint == null) return;

                var actor = await _userManager.FindByIdAsync(actorId);
                string actorName = actor?.UserName ?? "A user";

                string title = "Complaint Updated";
                string message = $"Complaint '{complaintTitle}' was updated by {actorName}.";

                // Notify Citizen
                if (complaint.CitizenId != actorId)
                {
                    await CreateAndSendAsync(complaint.CitizenId, title, message, NotificationType.Info, "Complaint", complaintId);
                }

                // Notify Assigned Employee
                if (!string.IsNullOrEmpty(complaint.AssignedEmployeeId) && complaint.AssignedEmployeeId != actorId)
                {
                    await CreateAndSendAsync(complaint.AssignedEmployeeId, title, message, NotificationType.Info, "Complaint", complaintId);
                }

                // Notify Department Manager(s)
                if (actor?.UserType != UserType.DepartmentManager)
                {
                    if (Enum.TryParse<Department>(complaint.DepartmentId, out var deptEnum))
                    {
                        var managers = await _userManager.Users
                            .Where(u => u.Department == deptEnum && u.UserType == UserType.DepartmentManager && u.Id != actorId)
                            .ToListAsync();

                        foreach (var manager in managers)
                        {
                            await CreateAndSendAsync(manager.Id, title, message, NotificationType.Info, "Complaint", complaintId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send update notification");
            }
        }

        // ==========================================
        //  PRIVATE HELPER TO STANDARDIZE EVERYTHING
        // ==========================================
        private async Task CreateAndSendAsync(
            string userId,
            string title,
            string message,
            NotificationType type,
            string relatedEntityType,
            Guid? relatedEntityId)
        {
            // 1. Save to Database (Persistence)
            var notificationId = await _notificationService.CreateNotificationAsync(
                userId, title, message, type, relatedEntityType, relatedEntityId
            );

            // 2. Retrieve the Full DTO (Structured Object)
            var dto = await _notificationService.GetNotificationByIdAsync(notificationId, userId);

            if (dto != null)
            {
                // 3. Send Object to SignalR (Real-time)
                await _hubContext.Clients.User(userId).SendAsync("ReceiveNotification", dto);

                // 4. Update the Badge Count
                var unreadCount = await _notificationService.GetUnreadCountAsync(userId);
                await _hubContext.Clients.User(userId).SendAsync("UnreadCountUpdated", unreadCount);

                // 5. Send Push Notification (Mobile)
                await _pushNotificationService.SendNotificationAsync(userId, title, message, new Dictionary<string, string>
                {
                    { "type", type.ToString() },
                    { "relatedEntityType", relatedEntityType ?? "" },
                    { "relatedEntityId", relatedEntityId?.ToString() ?? "" },
                    { "url", $"/complaints/{relatedEntityId}" } 
                });
            }
        }
    }
}