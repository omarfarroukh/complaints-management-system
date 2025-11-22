namespace CMS.Application.Exceptions;

public class ApiException : Exception
{
    public List<string>? Errors { get; set; }

    public ApiException() : base()
    {
    }

    public ApiException(string message) : base(message)
    {
    }

    public ApiException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ApiException(string message, List<string> errors) : base(message)
    {
        Errors = errors;
    }
}