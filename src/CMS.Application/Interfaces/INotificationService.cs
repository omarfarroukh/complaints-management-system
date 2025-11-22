using CMS.Application.DTOs;
using CMS.Domain.Common;

namespace CMS.Application.Interfaces
{
    public interface INotificationService
    {
        Task<Guid> CreateNotificationAsync(
            string userId,
            string title,
            string message,
            NotificationType type,
            string? relatedEntityType = null,
            Guid? relatedEntityId = null);

        Task<List<NotificationDto>> GetUnreadNotificationsAsync(string userId);

        Task<List<NotificationDto>> GetRecentNotificationsAsync(string userId, int count = 50);

        Task MarkAsReadAsync(Guid notificationId, string userId);

        Task MarkAllAsReadAsync(string userId);

        Task DeleteNotificationAsync(Guid notificationId, string userId);

        Task<int> GetUnreadCountAsync(string userId);

        Task<NotificationDto?> GetNotificationByIdAsync(Guid notificationId, string userId);
    }
}
