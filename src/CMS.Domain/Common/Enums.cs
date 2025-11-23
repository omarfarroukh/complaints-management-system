namespace CMS.Domain.Common
{
    public enum ComplaintStatus
    {
        Draft = 0,
        Submitted = 1,
        Pending = 2, // Alias for Submitted if needed, or keep distinct
        Assigned = 3,
        InProgress = 4,
        Resolved = 5,
        Closed = 6,
        Rejected = 7
    }

    public enum ComplaintPriority
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Critical = 3
    }

    public enum NotificationType
    {
        Info = 0,
        Success = 1,
        Warning = 2,
        Error = 3
    }
}
