using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicWebsite.Application.Interfaces.Media;

namespace MusicWebsite.Infrastructure.Media;

/// <summary>
/// Looks up lyrics on lyrics.ovh — free, no API key, one endpoint: <c>/v1/{artist}/{title}</c>.
///
/// Strictly a fallback behind LRCLIB. It returns plain text only, so there are no timestamps to
/// scroll with, and it needs an artist name to look anything up at all. It earns its place
/// because LRCLIB's API goes down for long stretches, and plain lyrics beat an empty panel.
///
/// Like every lyrics source here this is a lookup of published words, never a generation of them.
/// </summary>
public class LyricsOvhLyricsProvider : ILyricsProvider
{
    private const string SourceName = "lyrics.ovh";

    private readonly HttpClient _http;
    private readonly LyricsSettings _settings;
    private readonly LyricsSourceCircuitBreaker _breaker;
    private readonly ILogger<LyricsOvhLyricsProvider> _logger;

    public LyricsOvhLyricsProvider(HttpClient http, IOptions<LyricsSettings> settings,
        LyricsSourceCircuitBreaker breaker, ILogger<LyricsOvhLyricsProvider> logger)
    {
        _http = http;
        _settings = settings.Value;
        _breaker = breaker;
        _logger = logger;
    }

    public async Task<FoundLyrics?> FindAsync(string title, string? artist, string? album,
        int? durationInSeconds, CancellationToken cancellationToken = default)
    {
        // The endpoint is addressed by artist AND title — there is no title-only form.
        if (!_settings.UseLyricsOvh || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            return null;

        if (_breaker.IsTripped(SourceName))
        {
            _logger.LogDebug("Skipping lyrics.ovh for '{Title}' — marked unhealthy", title);
            return null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));

            var path = $"/v1/{Uri.EscapeDataString(artist.Trim())}/{Uri.EscapeDataString(title.Trim())}";
            using var response = await _http.GetAsync(path, timeout.Token);

            // 404 is the normal "not in the catalogue" answer — a healthy reply with no match.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _breaker.RecordSuccess(SourceName);
                return null;
            }

            // Any other non-2xx is the service failing, not answering.
            response.EnsureSuccessStatusCode();

            var record = await response.Content.ReadFromJsonSafeAsync<LyricsOvhResponse>(timeout.Token);
            _breaker.RecordSuccess(SourceName);

            var text = record?.Lyrics;
            return string.IsNullOrWhiteSpace(text) ? null : new FoundLyrics(text.Trim(), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _breaker.RecordFailure(SourceName);
            _logger.LogDebug("lyrics.ovh timed out for '{Title}'", title);
            return null;
        }
        catch (Exception ex)
        {
            _breaker.RecordFailure(SourceName);
            _logger.LogDebug(ex, "lyrics.ovh lookup failed for '{Title}'", title);
            return null;
        }
    }

    private sealed record LyricsOvhResponse
    {
        [JsonPropertyName("lyrics")] public string? Lyrics { get; init; }
    }
}
