using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicWebsite.Application.Interfaces.Media;

namespace MusicWebsite.Infrastructure.Media;

/// <summary>
/// Tries each lyrics source in turn and keeps the first real answer.
///
/// Order is quality-first: LRCLIB can supply timestamped lyrics that scroll with the song, so it
/// gets first refusal; lyrics.ovh only has plain text and catches what LRCLIB misses or can't
/// serve during an outage. Because it is a chain rather than a single source, the feature keeps
/// working when one service is down, and quietly upgrades to synced lyrics again when LRCLIB
/// recovers — with no code change or redeploy.
///
/// The whole chain sits under one wall-clock budget so a run of slow-but-not-failing services
/// can't add up to an unacceptable wait for the uploader.
/// </summary>
public class ChainedLyricsProvider : ILyricsProvider
{
    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly LyricsSettings _settings;
    private readonly ILogger<ChainedLyricsProvider> _logger;

    public ChainedLyricsProvider(LrcLibLyricsProvider lrcLib, LyricsOvhLyricsProvider lyricsOvh,
        IOptions<LyricsSettings> settings, ILogger<ChainedLyricsProvider> logger)
    {
        _providers = new ILyricsProvider[] { lrcLib, lyricsOvh };
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<FoundLyrics?> FindAsync(string title, string? artist, string? album,
        int? durationInSeconds, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled || string.IsNullOrWhiteSpace(title)) return null;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(_settings.TotalTimeoutSeconds));

        foreach (var provider in _providers)
        {
            // The overall budget is spent — stop rather than start a lookup that can't finish.
            if (budget.IsCancellationRequested) break;

            FoundLyrics? found;
            try
            {
                found = await provider.FindAsync(title, artist, album, durationInSeconds, budget.Token);
            }
            catch (Exception ex)
            {
                // Providers already swallow their own failures; this is belt and braces so one
                // misbehaving source can never fail an upload that has otherwise succeeded.
                _logger.LogDebug(ex, "Lyrics source {Provider} threw for '{Title}'", provider.GetType().Name, title);
                continue;
            }

            if (found is null || found.IsEmpty) continue;

            _logger.LogInformation("Lyrics for '{Title}' found via {Provider} (synced: {Synced})",
                title, provider.GetType().Name, found.Synced is not null);
            return found;
        }

        return null;
    }
}
