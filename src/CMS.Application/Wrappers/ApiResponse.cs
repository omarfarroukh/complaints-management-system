namespace CMS.Application.Wrappers;

public class ApiResponse<T>
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty; // Initialize to avoid null warning
    public List<string>? Errors { get; set; } // "?" means it allows nulls
    public T? Data { get; set; }              // "?" means it allows nulls

    public ApiResponse() { }

    // Constructor for Success
    public ApiResponse(T? data, string message = "Success")
    {
        Succeeded = true;
        Message = message;
        Data = data;
        Errors = null;
    }

    // Constructor for Failure
    public ApiResponse(string message)
    {
        Succeeded = false;
        Message = message;
        Errors = new List<string> { message };
        Data = default; // Sets Data to null
    }
}