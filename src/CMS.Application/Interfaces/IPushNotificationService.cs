namespace CMS.Application.Interfaces
{
    public interface IPushNotificationService
    {
        Task SendNotificationAsync(string userId, string title, string body, Dictionary<string, string>? data = null);
        Task RegisterDeviceAsync(string userId, string token, string platform);
        Task UnregisterDeviceAsync(string userId, string token);
        Task<bool> IsDeviceRegisteredAsync(string userId, string token);

    }
}
