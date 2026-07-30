using MusicWebsite.Application.Models;

namespace MusicWebsite.Application.Interfaces.Media;

/// <summary>Lyrics found for a track. Either field may be null when only one form is published.</summary>
/// <param name="Plain">Plain text, line per line.</param>
/// <param name="Synced">LRC format with <c>[mm:ss.xx]</c> timestamps, for scrolling in time.</param>
public sealed record FoundLyrics(string? Plain, string? Synced)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Plain) && string.IsNullOrWhiteSpace(Synced);
}

/// <summary>
/// Looks up real, published lyrics for a track.
///
/// Deliberately a lookup and not a generator: no model can reproduce a copyrighted song's words
/// reliably, and an invented verse scrolling against a song people know is worse than showing
/// nothing at all. Lyrics come from a real database or they don't come.
/// </summary>
public interface ILyricsProvider
{
    /// <summary>
    /// Best-effort lookup. Returns null when nothing matches, when the service is down, or when
    /// it is disabled — callers must treat all three the same and carry on.
    /// </summary>
    Task<FoundLyrics?> FindAsync(string title, string? artist, string? album, int? durationInSeconds,
        CancellationToken cancellationToken = default);
}
