using CMS.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using nClam;

namespace CMS.Infrastructure.Services
{
    public class ClamAvVirusScanService : IVirusScanService
    {
        private readonly string _host;
        private readonly int _port;
        private readonly ILogger<ClamAvVirusScanService> _logger;

        public ClamAvVirusScanService(IConfiguration config, ILogger<ClamAvVirusScanService> logger)
        {
            _host = config["CLAMAV_HOST"] ?? "localhost";
            _port = int.Parse(config["CLAMAV_PORT"] ?? "3310");
            _logger = logger;
        }

        public async Task<string?> ScanStreamAsync(Stream fileStream, CancellationToken cancellationToken = default)
        {
            try
            {
                var clam = new ClamClient(_host, _port);
                // Set max size (e.g., 100MB) to prevent timeouts on large files
                clam.MaxStreamSize = 100 * 1024 * 1024; 

                var result = await clam.SendAndScanFileAsync(fileStream, cancellationToken);

                if (result.Result == ClamScanResults.VirusDetected)
                {
                    return result.InfectedFiles?.FirstOrDefault()?.VirusName ?? "Unknown Virus";
                }

                if (result.Result == ClamScanResults.Error)
                {
                    _logger.LogError($"ClamAV Error: {result.RawResult}");
                    throw new Exception("ClamAV scan failed."); // Throwing lets Hangfire retry
                }

                return null; // Null means Clean
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to ClamAV.");
                throw; 
            }
        }
    }
}