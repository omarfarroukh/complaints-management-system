namespace CMS.Domain.Common
{
    public enum ComplaintStatus
    {
        Pending = 0,
        Assigned = 1,
        InProgress = 2,
        Resolved = 3,
        Closed = 4
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
