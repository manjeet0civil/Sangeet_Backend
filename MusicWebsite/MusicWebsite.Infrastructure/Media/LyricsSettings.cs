namespace MusicWebsite.Infrastructure.Media;

public class LyricsSettings
{
    public const string SectionName = "Lyrics";

    /// <summary>
    /// Master switch. Off means no lyrics lookup happens at all and uploads behave exactly as
    /// they did before — useful if the lyrics services are having a bad day and you want the
    /// latency back.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// LRCLIB — free, open, no API key, and the only source here that publishes timestamped
    /// <c>.lrc</c> lyrics that can scroll in time with playback. Tried first for that reason.
    /// </summary>
    public string LrcLibBaseUrl { get; set; } = "https://lrclib.net";

    /// <summary>
    /// lyrics.ovh — free, no API key, plain text only. A fallback, not a replacement: it can't
    /// give synced lyrics and it needs an artist name. Worth having because LRCLIB's API has
    /// extended outages, and plain lyrics beat none.
    /// </summary>
    public string LyricsOvhBaseUrl { get; set; } = "https://api.lyrics.ovh";

    /// <summary>Set false to skip a source without turning the whole feature off.</summary>
    public bool UseLrcLib { get; set; } = true;
    public bool UseLyricsOvh { get; set; } = true;

    /// <summary>
    /// Per-source cap. Deliberately short: the lookup runs after the song is already saved, so a
    /// slow source costs the uploader waiting time for something entirely optional.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 6;

    /// <summary>
    /// Ceiling across every source tried. Stops a chain of slow-but-not-quite-timing-out
    /// services from adding up to an unacceptable wait.
    /// </summary>
    public int TotalTimeoutSeconds { get; set; } = 15;
}
