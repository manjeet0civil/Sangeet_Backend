namespace MusicWebsite.Application.Models;

/// <summary>
/// Transport for an uploaded file passed from the API layer to a service, without leaking
/// ASP.NET's IFormFile into the Application layer.
/// </summary>
public class UploadFileInput
{
    public required Stream Content { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public long Length { get; init; }
}
