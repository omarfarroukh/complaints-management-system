using CMS.Application.DTOs;

namespace CMS.Application.Interfaces
{
    public interface IComplaintLockService
    {
        Task<bool> AcquireLockAsync(Guid complaintId, string userId, int ttlSeconds = 300);
        Task<bool> ReleaseLockAsync(Guid complaintId, string userId);
        Task<string?> GetCurrentLockHolderAsync(Guid complaintId);
        Task<ComplaintLockDto?> GetLockInfoAsync(Guid complaintId);
    }
}
