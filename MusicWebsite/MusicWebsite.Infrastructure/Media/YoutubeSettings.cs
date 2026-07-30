namespace MusicWebsite.Infrastructure.Media;

/// <summary>Configuration for YouTube import (which extractor to use, and where yt-dlp lives).</summary>
public class YoutubeSettings
{
    public const string SectionName = "Youtube";

    /// <summary>"YtDlp" (robust, needs yt-dlp.exe) or "YoutubeExplode" (pure .NET, less reliable).</summary>
    public string Provider { get; set; } = "YoutubeExplode";

    /// <summary>Path to yt-dlp.exe. Absolute, or resolvable from the app's working directory / PATH.</summary>
    public string YtDlpPath { get; set; } = "yt-dlp";

    /// <summary>Max seconds to allow a single yt-dlp download before giving up.</summary>
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// Optional HTTP proxy, applied to <b>yt-dlp only</b> — nothing else in the application
    /// routes through it. The database, Backblaze B2 and every API request keep using the
    /// server's own connection.
    ///
    /// Why it exists: YouTube blocks datacenter IP ranges and answers with "Sign in to confirm
    /// you're not a bot" for every video, including public ones. Sending just the YouTube
    /// traffic through a residential proxy is what gets around that.
    ///
    /// Format: <c>http://user:password@host:port</c>. Leave empty to make direct connections.
    /// Set it via the environment variable <c>Youtube__ProxyUrl</c> — it contains credentials,
    /// so it must never be committed to the repository.
    /// </summary>
    public string? ProxyUrl { get; set; }
}
