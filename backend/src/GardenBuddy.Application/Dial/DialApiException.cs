using System.Net;

namespace GardenBuddy.Application.Dial;

public sealed class DialApiException : Exception
{
    public DialApiException(HttpStatusCode statusCode, string message, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ResponseBody { get; }
}
