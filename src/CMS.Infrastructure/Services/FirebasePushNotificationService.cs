using CMS.Application.Interfaces;
using CMS.Domain.Entities;
using CMS.Infrastructure.Persistence;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CMS.Infrastructure.Services
{
    public class FirebasePushNotificationService : IPushNotificationService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<FirebasePushNotificationService> _logger;
        private readonly bool _isEnabled;

        public FirebasePushNotificationService(
            AppDbContext context,
            IConfiguration configuration,
            ILogger<FirebasePushNotificationService> logger)
        {
            _context = context;
            _logger = logger;

            // Initialize Firebase App if not already done
            if (FirebaseApp.DefaultInstance == null)
            {
                var credentialsPath = configuration["Firebase:CredentialsPath"];
                // We check specifically for the file in the base directory or bin output
                if (!string.IsNullOrEmpty(credentialsPath) && File.Exists(credentialsPath))
                {
                    try
                    {
                        FirebaseApp.Create(new AppOptions()
                        {
                            Credential = GoogleCredential.FromFile(credentialsPath)
                        });
                        _isEnabled = true;
                        _logger.LogInformation("Firebase App initialized successfully.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to initialize Firebase App.");
                        _isEnabled = false;
                    }
                }
                else
                {
                    _logger.LogWarning("Firebase credentials not found at {Path}. Push notifications will be disabled.", credentialsPath);
                    _isEnabled = false;
                }
            }
            else
            {
                _isEnabled = true;
            }
        }

        public async Task RegisterDeviceAsync(string userId, string token, string platform)
        {
            var existingDevice = await _context.UserDevices
                .FirstOrDefaultAsync(d => d.DeviceToken == token);

            if (existingDevice != null)
            {
                // Update existing device if user changed or just update timestamp
                existingDevice.UserId = userId;
                existingDevice.LastUpdated = DateTime.UtcNow;
                existingDevice.Platform = platform;
                _context.UserDevices.Update(existingDevice);
            }
            else
            {
                // Add new device
                var newDevice = new UserDevice
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    DeviceToken = token,
                    Platform = platform,
                    LastUpdated = DateTime.UtcNow
                };
                _context.UserDevices.Add(newDevice);
            }

            await _context.SaveChangesAsync();
        }

        public async Task UnregisterDeviceAsync(string userId, string token)
        {
            var device = await _context.UserDevices
                .FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceToken == token);

            if (device != null)
            {
                _context.UserDevices.Remove(device);
                await _context.SaveChangesAsync();
            }
        }

        public async Task SendNotificationAsync(string userId, string title, string body, Dictionary<string, string>? data = null)
        {
            if (!_isEnabled) return;

            var devices = await _context.UserDevices
                .Where(d => d.UserId == userId)
                .ToListAsync();

            if (!devices.Any()) return;

            // --- KEY CHANGE FOR DUPLICATES ---
            // We move Title and Body into the DATA payload.
            // We set the actual 'Notification' property to null.
            // This prevents the browser from auto-displaying a notification (Data-Only Message).
            
            var finalData = data ?? new Dictionary<string, string>();
            finalData["title"] = title;
            finalData["body"] = body;
            finalData["url"] = "/complaints"; 

            var message = new MulticastMessage()
            {
                Tokens = devices.Select(d => d.DeviceToken).ToList(),
                
                // IMPORTANT: Keep this null to prevent duplicates!
                Notification = null, 
                
                Data = finalData
            };

            try
            {
                var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);

                if (response.FailureCount > 0)
                {
                    var failedTokens = new List<string>();
                    for (var i = 0; i < response.Responses.Count; i++)
                    {
                        if (!response.Responses[i].IsSuccess)
                        {
                            var error = response.Responses[i].Exception.MessagingErrorCode;
                            if (error == MessagingErrorCode.Unregistered || error == MessagingErrorCode.InvalidArgument)
                            {
                                failedTokens.Add(devices[i].DeviceToken);
                            }
                        }
                    }

                    if (failedTokens.Any())
                    {
                        await RemoveInvalidTokensAsync(failedTokens);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send push notification to user {UserId}", userId);
            }
        }
        public async Task<bool> IsDeviceRegisteredAsync(string userId, string token)
        {
            return await _context.UserDevices
                .AnyAsync(d => d.UserId == userId && d.DeviceToken == token);
        }
        private async Task RemoveInvalidTokensAsync(List<string> tokens)
        {
            var invalidDevices = await _context.UserDevices
                .Where(d => tokens.Contains(d.DeviceToken))
                .ToListAsync();

            if (invalidDevices.Any())
            {
                _context.UserDevices.RemoveRange(invalidDevices);
                await _context.SaveChangesAsync();
            }
        }
    }
}