namespace CMS.Application.Interfaces
{
    public interface IAttachmentScanningJob
    {
        Task ExecuteAsync(Guid attachmentId);
    }
}