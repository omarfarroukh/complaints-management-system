using Microsoft.AspNetCore.Http;

namespace CMS.Api.Helpers
{
    public static class UrlHelper
    {
        public static string? ToAbsoluteUrl(this string? relativePath, HttpRequest request)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;

            // If it's already a full URL (e.g. S3), return as is
            if (relativePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)) 
                return relativePath;

            var baseUrl = $"{request.Scheme}://{request.Host}";
            
            // Normalize slashes
            var formattedPath = relativePath.Replace("\\", "/").TrimStart('/');

            // Strip wwwroot if stored in DB
            if (formattedPath.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase))
            {
                formattedPath = formattedPath.Substring(8);
            }

            return $"{baseUrl}/{formattedPath}";
        }
    }
}