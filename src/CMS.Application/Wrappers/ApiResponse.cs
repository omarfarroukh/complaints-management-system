using System.Text.Json.Serialization; // 👈 Import this

namespace CMS.Application.Wrappers;

public class ApiResponse<T>
{
    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    public ApiResponse() { }

    public ApiResponse(T? data, string message = "Success")
    {
        Succeeded = true;
        Message = message;
        Data = data;
        Errors = null;
    }

    public ApiResponse(string message)
    {
        Succeeded = false;
        Message = message;
        Errors = new List<string> { message };
        Data = default;
    }
}