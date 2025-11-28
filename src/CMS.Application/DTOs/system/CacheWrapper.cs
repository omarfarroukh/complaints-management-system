namespace CMS.Application.DTOs.System;

/// <summary>
/// Wraps the cached content to preserve Content-Type headers
/// </summary>
public class CacheWrapper
{
    public string Content { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/json";
}