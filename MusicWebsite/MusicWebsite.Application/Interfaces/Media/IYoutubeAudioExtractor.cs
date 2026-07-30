using MusicWebsite.Application.Models;

namespace MusicWebsite.Application.Interfaces.Media;

/// <summary>
/// Extracts audio (and metadata) from a YouTube video. The implementation downloads only the
/// audio-only stream — never the full video — to a temporary file, so the server stays light;
/// the caller is responsible for uploading it to permanent storage and disposing the result.
/// </summary>
public interface IYoutubeAudioExtractor
{
    /// <summary>Parses the canonical 11-char video id from a URL locally (no network), or null if invalid.</summary>
    string? TryGetVideoId(string url);

    /// <summary>Fast metadata-only lookup (title, author, duration, thumbnail URL) — no download.</summary>
    Task<YoutubePreview> GetPreviewAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the best audio-only stream to a temp file and returns it alongside metadata.
    /// Dispose the returned value to delete the temporary file(s).
    /// </summary>
    Task<ExtractedYoutubeAudio> ExtractAsync(string url, CancellationToken cancellationToken = default);
}
