namespace CMS.Application.Interfaces
{
    public interface IVirusScanService
    {
        /// <summary>
        /// Scans a file stream and returns the virus name if found, or null if clean.
        /// </summary>
        Task<string?> ScanStreamAsync(Stream fileStream, CancellationToken cancellationToken = default);
    }
}