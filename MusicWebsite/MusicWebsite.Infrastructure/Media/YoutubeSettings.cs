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
}
