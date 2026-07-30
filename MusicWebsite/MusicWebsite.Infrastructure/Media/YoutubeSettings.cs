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
    /// Master switch for YouTube import (env: <c>Youtube__UseProxy</c>).
    ///
    /// <b>true</b>  — imports run, with every yt-dlp call routed through <see cref="ProxyUrl"/>.<br/>
    /// <b>false</b> — imports are refused up front with a clear message.
    ///
    /// It is a single switch rather than two because the feature does not work without the proxy:
    /// YouTube blocks datacenter IPs, so a direct call from the server fails on every video with
    /// "Sign in to confirm you're not a bot". Refusing immediately is honest and instant, instead
    /// of making the user wait for a download that cannot succeed.
    ///
    /// Defaults to false so a deployment without a proxy configured behaves predictably.
    /// </summary>
    public bool UseProxy { get; set; }

    /// <summary>
    /// The proxy used when <see cref="UseProxy"/> is true, applied to <b>yt-dlp only</b> — nothing
    /// else in the application routes through it. The database, Backblaze B2 and every API request
    /// keep using the server's own connection.
    ///
    /// Format: <c>socks5h://user:password@host:port</c> or <c>http://user:password@host:port</c>.
    /// Prefer <c>socks5h</c> over <c>socks5</c> so DNS is resolved at the proxy rather than on the
    /// server — otherwise the lookup leaks out on the blocked connection.
    ///
    /// Set via the environment variable <c>Youtube__ProxyUrl</c>. It contains credentials, so it
    /// must never be committed to the repository.
    /// </summary>
    public string? ProxyUrl { get; set; }
}
