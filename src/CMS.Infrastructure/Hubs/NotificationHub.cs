using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CMS.Infrastructure.Hubs
{
    [Authorize]
    public class NotificationHub : Hub
    {
        private readonly INotificationService _notificationService;

        public NotificationHub(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                // Send unread count when user connects
                var unreadCount = await _notificationService.GetUnreadCountAsync(userId);
                await Clients.Caller.SendAsync("UnreadCountUpdated", unreadCount);
                
                // Auto-join department group if user has DepartmentId claim
                var departmentIdClaim = Context.User?.FindFirst("DepartmentId");
                if (departmentIdClaim != null && !string.IsNullOrEmpty(departmentIdClaim.Value))
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, departmentIdClaim.Value);
                }
            }
            await base.OnConnectedAsync();
        }

        // Client → Server: Get unread notifications
        public async Task GetUnreadNotifications()
        {
            var userId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(userId)) return;

            var notifications = await _notificationService.GetUnreadNotificationsAsync(userId);
            await Clients.Caller.SendAsync("NotificationsLoaded", notifications);
        }

        // Client → Server: Get recent notifications with pagination
        public async Task GetRecentNotifications(int count = 50)
        {
            var userId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(userId)) return;

            var notifications = await _notificationService.GetRecentNotificationsAsync(userId, count);
            await Clients.Caller.SendAsync("NotificationsLoaded", notifications);
        }

        // Client → Server: Mark notification as read
        public async Task MarkAsRead(Guid notificationId)
        {
            var userId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(userId)) return;

            try
            {
                await _notificationService.MarkAsReadAsync(notificationId, userId);

                // Confirm to client
                await Clients.Caller.SendAsync("NotificationMarkedAsRead", notificationId);

                // Update unread count
                var unreadCount = await _notificationService.GetUnreadCountAsync(userId);
                await Clients.Caller.SendAsync("UnreadCountUpdated", unreadCount);
            }
            catch (KeyNotFoundException)
            {
                await Clients.Caller.SendAsync("Error", "Notification not found");
            }
        }

        // Client → Server: Mark all as read
        public async Task MarkAllAsRead()
        {
            var userId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(userId)) return;

            await _notificationService.MarkAllAsReadAsync(userId);

            // Notify client
            await Clients.Caller.SendAsync("AllNotificationsMarkedAsRead");
            await Clients.Caller.SendAsync("UnreadCountUpdated", 0);
        }

        // Client → Server: Delete notification
        public async Task DeleteNotification(Guid notificationId)
        {
            var userId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(userId)) return;

            try
            {
                await _notificationService.DeleteNotificationAsync(notificationId, userId);

                // Confirm deletion
                await Clients.Caller.SendAsync("NotificationDeleted", notificationId);

                // Update unread count
                var unreadCount = await _notificationService.GetUnreadCountAsync(userId);
                await Clients.Caller.SendAsync("UnreadCountUpdated", unreadCount);
            }
            catch (KeyNotFoundException)
            {
                await Clients.Caller.SendAsync("Error", "Notification not found");
            }
        }

        // Client → Server: Get unread count
        public async Task GetUnreadCount()
        {
            var userId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(userId)) return;

            var count = await _notificationService.GetUnreadCountAsync(userId);
            await Clients.Caller.SendAsync("UnreadCountUpdated", count);
        }

        // Server → Client methods (called by other services):
        // - ReceiveNotification(NotificationDto)
        // - NotificationsLoaded(List<NotificationDto>)
        // - UnreadCountUpdated(int)
        // - NotificationMarkedAsRead(Guid)
        // - AllNotificationsMarkedAsRead()
        // - NotificationDeleted(Guid)
        // - Error(string)

        // Legacy methods for department/user messaging (keep for backward compatibility)
        public async Task SendMessageToUser(string userId, string message)
        {
            await Clients.User(userId).SendAsync("ReceiveMessage", message);
        }

        public async Task SendMessageToDepartment(string departmentId, string message)
        {
            await Clients.Group(departmentId).SendAsync("ReceiveDepartmentMessage", message);
        }

        public async Task JoinDepartmentGroup(string departmentId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, departmentId);
        }

        public async Task LeaveDepartmentGroup(string departmentId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, departmentId);
        }
    }
}
