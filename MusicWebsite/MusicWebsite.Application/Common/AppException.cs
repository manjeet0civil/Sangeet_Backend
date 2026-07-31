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

    /// <summary>
    /// Keeps the underlying failure attached for the logs while the caller only sees
    /// <paramref name="message"/> — used where the real cause (a rejected Google token, say)
    /// is worth recording but not worth telling the client.
    /// </summary>
    public AppException(string message, int statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
