using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicWebsite.Application.Interfaces.Media;

namespace MusicWebsite.Infrastructure.Media;

/// <summary>
/// Looks up lyrics on LRCLIB (https://lrclib.net) — free, open, no API key, no rate-limit
/// registration, and crucially it publishes <c>.lrc</c> synced lyrics with real timestamps taken
/// from real recordings.
///
/// Two-step lookup. The exact endpoint matches on artist + track + album + duration together and
/// is the only one that can be trusted blindly; when it misses we fall back to a search and accept
/// a hit only if its duration is close to ours, which stops a remix or a live version being
/// pinned to the studio track.
///
/// Nothing here ever throws at the caller. A miss, a timeout and an outage are all "no lyrics".
/// </summary>
public class LrcLibLyricsProvider : ILyricsProvider
{
    /// <summary>How far a search hit's length may differ from ours before we reject it.</summary>
    private const int DurationToleranceSeconds = 5;

    /// <summary>Key this source is tracked under in the shared circuit breaker.</summary>
    private const string SourceName = "lrclib";

    private readonly HttpClient _http;
    private readonly LyricsSettings _settings;
    private readonly LyricsSourceCircuitBreaker _breaker;
    private readonly ILogger<LrcLibLyricsProvider> _logger;

    public LrcLibLyricsProvider(HttpClient http, IOptions<LyricsSettings> settings,
        LyricsSourceCircuitBreaker breaker, ILogger<LrcLibLyricsProvider> logger)
    {
        _settings = settings.Value;
        _breaker = breaker;
        _logger = logger;
        _http = http;
    }

    public async Task<FoundLyrics?> FindAsync(string title, string? artist, string? album,
        int? durationInSeconds, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled || !_settings.UseLrcLib || string.IsNullOrWhiteSpace(title)) return null;

        // LRCLIB has multi-hour outages. While it's down, skip it outright rather than pay its
        // timeout on every upload before falling through to the next source.
        if (_breaker.IsTripped(SourceName))
        {
            _logger.LogDebug("Skipping LRCLIB for '{Title}' — marked unhealthy", title);
            return null;
        }

        try
        {
            // Don't let a slow lyrics service hold up the response; the song is already saved.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));

            var found = await GetExactAsync(title, artist, album, durationInSeconds, timeout.Token)
                ?? await SearchAsync(title, artist, durationInSeconds, timeout.Token);

            // Reaching here means the service answered — including a clean "not in the catalogue",
            // which is a healthy response and must not count against it.
            _breaker.RecordSuccess(SourceName);
            return found;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _breaker.RecordFailure(SourceName);
            _logger.LogDebug("Lyrics lookup timed out for '{Title}'", title);
            return null;
        }
        catch (Exception ex)
        {
            _breaker.RecordFailure(SourceName);
            _logger.LogDebug(ex, "Lyrics lookup failed for '{Title}'", title);
            return null;
        }
    }

    /// <summary>The precise endpoint: everything must line up, so a hit needs no further checking.</summary>
    private async Task<FoundLyrics?> GetExactAsync(string title, string? artist, string? album,
        int? duration, CancellationToken cancellationToken)
    {
        // Without an artist this degrades to a title-only guess, which the search path handles better.
        if (string.IsNullOrWhiteSpace(artist)) return null;

        var query = $"/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        if (!string.IsNullOrWhiteSpace(album)) query += $"&album_name={Uri.EscapeDataString(album)}";
        if (duration is > 0) query += $"&duration={duration}";

        using var response = await _http.GetAsync(query, cancellationToken);

        // 404 is the normal "we don't have this song" answer, not a fault — return without
        // complaint so the circuit breaker still counts the service as healthy.
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        // Anything else non-2xx (LRCLIB's gateway serves 502/504 during its outages) is a genuine
        // failure and must reach the caller's catch so the breaker records it.
        response.EnsureSuccessStatusCode();

        var record = await response.Content.ReadFromJsonSafeAsync<LrcLibRecord>(cancellationToken);
        return ToLyrics(record);
    }

    /// <summary>Looser lookup, with the duration check doing the quality control.</summary>
    private async Task<FoundLyrics?> SearchAsync(string title, string? artist, int? duration,
        CancellationToken cancellationToken)
    {
        var query = $"/api/search?track_name={Uri.EscapeDataString(title)}";
        if (!string.IsNullOrWhiteSpace(artist)) query += $"&artist_name={Uri.EscapeDataString(artist)}";

        using var response = await _http.GetAsync(query, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonSafeAsync<List<LrcLibRecord>>(cancellationToken);
        if (results is null || results.Count == 0) return null;

        // With a known duration, insist on a close match — same title by a different artist, or a
        // 9-minute live cut of a 4-minute song, would otherwise be accepted and shown as fact.
        var match = duration is > 0
            ? results.FirstOrDefault(r => r.Duration is > 0 && Math.Abs(r.Duration.Value - duration.Value) <= DurationToleranceSeconds)
            : results[0];

        return ToLyrics(match);
    }

    private static FoundLyrics? ToLyrics(LrcLibRecord? record)
    {
        // Instrumental tracks are catalogued with no words at all — that's a definitive answer,
        // but there is nothing to store.
        if (record is null || record.Instrumental) return null;

        var lyrics = new FoundLyrics(
            Blank(record.PlainLyrics),
            Blank(record.SyncedLyrics));

        return lyrics.IsEmpty ? null : lyrics;
    }

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>One LRCLIB catalogue entry.</summary>
    private sealed record LrcLibRecord
    {
        [JsonPropertyName("trackName")] public string? TrackName { get; init; }
        [JsonPropertyName("artistName")] public string? ArtistName { get; init; }
        [JsonPropertyName("duration")] public double? Duration { get; init; }
        [JsonPropertyName("instrumental")] public bool Instrumental { get; init; }
        [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; init; }
        [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; init; }
    }
}

internal static class HttpContentJsonExtensions
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Deserialises a response body, returning null rather than throwing when the service replies
    /// with something unexpected (an HTML error page, a truncated body).
    /// </summary>
    public static async Task<T?> ReadFromJsonSafeAsync<T>(this HttpContent content, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
