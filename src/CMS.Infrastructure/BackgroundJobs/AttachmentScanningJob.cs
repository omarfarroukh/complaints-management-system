using CMS.Application.Interfaces;
using CMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace CMS.Infrastructure.BackgroundJobs
{
    public class AttachmentScanningJob : IAttachmentScanningJob
    {
        private readonly AppDbContext _context;
        private readonly IFileStorageService _fileStorage;
        private readonly IVirusScanService _scanner;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<AttachmentScanningJob> _logger;

        public AttachmentScanningJob(
            AppDbContext context,
            IFileStorageService fileStorage,
            IVirusScanService scanner,
            IWebHostEnvironment env,
            ILogger<AttachmentScanningJob> logger)
        {
            _context = context;
            _fileStorage = fileStorage;
            _scanner = scanner;
            _env = env;
            _logger = logger;
        }

        public async Task ExecuteAsync(Guid attachmentId)
        {
            _logger.LogInformation($"--- STARTING SCAN JOB FOR: {attachmentId} ---");

            var attachment = await _context.ComplaintAttachments.FindAsync(attachmentId);
            if (attachment == null)
            {
                _logger.LogError("Attachment record not found in DB.");
                return;
            }

            // DEBUG LOGGING
            _logger.LogInformation($"DB FilePath: {attachment.FilePath}");

            // 1. Resolve Physical Path
            string webRootPath = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            _logger.LogInformation($"WebRootPath: {webRootPath}");

            // Sanitize path
            string relativePath = attachment.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.Combine(webRootPath, relativePath);

            _logger.LogInformation($"Looking for file at: {fullPath}");

            if (!File.Exists(fullPath))
            {
                // CHECK IF FOLDER EXISTS AT LEAST
                var directory = Path.GetDirectoryName(fullPath);
                bool dirExists = Directory.Exists(directory);
                _logger.LogWarning($"❌ FILE NOT FOUND! Directory exists? {dirExists}. Path: {fullPath}");

                // List files in the directory to see what IS there (if directory exists)
                if (dirExists)
                {
                    var files = Directory.GetFiles(directory!);
                    _logger.LogInformation($"Files found in that folder: {string.Join(", ", files)}");
                }
                return;
            }

            // 2. Scan
            _logger.LogInformation("File found. Starting ClamAV Stream...");

            using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // Measure time
                var watch = System.Diagnostics.Stopwatch.StartNew();

                var virusName = await _scanner.ScanStreamAsync(stream);

                watch.Stop();
                _logger.LogInformation($"ClamAV finished in {watch.ElapsedMilliseconds}ms.");

                if (virusName != null)
                {
                    _logger.LogCritical($"🦠 VIRUS DETECTED: {virusName}. Deleting...");
                    stream.Close();
                    await _fileStorage.DeleteFileAsync(attachment.FilePath);
                    _context.ComplaintAttachments.Remove(attachment);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    _logger.LogInformation("✅ File is CLEAN. Updating DB.");
                    attachment.IsScanned = true;
                    attachment.ScanResult = "Clean";
                    await _context.SaveChangesAsync();
                }
            }
        }
    }
}