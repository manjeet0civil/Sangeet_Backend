namespace MusicWebsite.Application.Common;

/// <summary>
/// Represents an expected business/validation failure that maps to a specific HTTP status.
/// Infrastructure translates SQL THROW errors (50001-50044) into this type so the API layer
/// stays free of database concerns.
/// </summary>
public class AppException : Exception
{
    public int StatusCode { get; }

    public AppException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }
}
