namespace MusicWebsite.Application.Models;

/// <summary>Lightweight metadata for previewing a YouTube video before importing it.</summary>
public record YoutubePreview(string Title, string? Author, int? DurationInSeconds, string? ThumbnailUrl);

/// <summary>
/// The result of extracting audio from a YouTube video. Holds temp file paths on the server;
/// call <see cref="OpenAudio"/> / <see cref="OpenThumbnail"/> to stream them to storage, then
/// dispose to delete the temp files. Never keeps anything permanently on the server.
/// </summary>
public sealed class ExtractedYoutubeAudio : IAsyncDisposable
{
    public required string Title { get; init; }
    public string? Author { get; init; }
    public int? DurationInSeconds { get; init; }

    public required string AudioFilePath { get; init; }
    public required string AudioFileName { get; init; }
    public required string AudioContentType { get; init; }

    public string? ThumbnailFilePath { get; init; }
    public string? ThumbnailFileName { get; init; }
    public string? ThumbnailContentType { get; init; }

    /// <summary>Optional temp directory to remove entirely on dispose (used by the yt-dlp extractor).</summary>
    public string? TempDirectory { get; init; }

    public Stream OpenAudio() => File.OpenRead(AudioFilePath);

    public Stream? OpenThumbnail() =>
        string.IsNullOrEmpty(ThumbnailFilePath) ? null : File.OpenRead(ThumbnailFilePath);

    public ValueTask DisposeAsync()
    {
        TryDelete(AudioFilePath);
        TryDelete(ThumbnailFilePath);
        TryDeleteDir(TempDirectory);
        return ValueTask.CompletedTask;
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort temp cleanup */ }
    }

    private static void TryDeleteDir(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
