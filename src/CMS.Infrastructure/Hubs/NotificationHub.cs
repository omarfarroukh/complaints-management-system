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
                // 1. Send Unread Count
                var unreadCount = await _notificationService.GetUnreadCountAsync(userId);
                await Clients.Caller.SendAsync("UnreadCountUpdated", unreadCount);

                // 2. Auto-join Department Group
                var departmentId = Context.User?.FindFirst("DepartmentId")?.Value;
                if (!string.IsNullOrEmpty(departmentId))
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, departmentId);
                }
            }
            await base.OnConnectedAsync();
        }

        // --- CLIENT CALLABLE METHODS (Must match Angular .invoke() names exactly) ---

        // 1. Get Unread Count (The one that was missing/erroring)
        public async Task GetUnreadCount()
        {
            var userId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(userId)) return;

            var count = await _notificationService.GetUnreadCountAsync(userId);
            await Clients.Caller.SendAsync("UnreadCountUpdated", count);
        }

        // 2. Get Recent Notifications
        public async Task GetRecentNotifications(int count = 50)
        {
            var userId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(userId)) return;

            var notifications = await _notificationService.GetRecentNotificationsAsync(userId, count);
            await Clients.Caller.SendAsync("NotificationsLoaded", notifications);
        }

        // 3. Mark As Read
        public async Task MarkAsRead(Guid notificationId)
        {
            var userId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(userId)) return;

            await _notificationService.MarkAsReadAsync(notificationId, userId);
            await Clients.Caller.SendAsync("NotificationMarkedAsRead", notificationId);

            // Push update to count
            var unreadCount = await _notificationService.GetUnreadCountAsync(userId);
            await Clients.Caller.SendAsync("UnreadCountUpdated", unreadCount);
        }

        // 4. Mark All Read
        public async Task MarkAllAsRead()
        {
            var userId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(userId)) return;

            await _notificationService.MarkAllAsReadAsync(userId);
            await Clients.Caller.SendAsync("AllNotificationsMarkedAsRead");
            await Clients.Caller.SendAsync("UnreadCountUpdated", 0);
        }

        // 5. Delete
        public async Task DeleteNotification(Guid notificationId)
        {
            var userId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(userId)) return;

            await _notificationService.DeleteNotificationAsync(notificationId, userId);
            await Clients.Caller.SendAsync("NotificationDeleted", notificationId);

            var unreadCount = await _notificationService.GetUnreadCountAsync(userId);
            await Clients.Caller.SendAsync("UnreadCountUpdated", unreadCount);
        }
    }
}