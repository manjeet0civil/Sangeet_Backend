using Microsoft.Extensions.Logging;
using MusicWebsite.Application.Common;
using MusicWebsite.Application.Interfaces.Media;
using MusicWebsite.Application.Models;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace MusicWebsite.Infrastructure.Media;

/// <summary>
/// Extracts audio from YouTube using the pure-.NET <c>YoutubeExplode</c> library — no external
/// binaries (yt-dlp / ffmpeg) required. Downloads only the highest-bitrate audio-only stream to a
/// temp file (never the full video), so the server stays light. The returned
/// <see cref="ExtractedYoutubeAudio"/> deletes its temp file(s) on dispose.
/// </summary>
public class YoutubeExplodeAudioExtractor : IYoutubeAudioExtractor
{
    private static readonly HttpClient Http = new();
    private readonly YoutubeClient _youtube = new();
    private readonly ILogger<YoutubeExplodeAudioExtractor> _logger;

    public YoutubeExplodeAudioExtractor(ILogger<YoutubeExplodeAudioExtractor> logger) => _logger = logger;

    public string? TryGetVideoId(string url)
    {
        var id = VideoId.TryParse(url);
        return id?.Value;
    }

    public async Task<YoutubePreview> GetPreviewAsync(string url, CancellationToken cancellationToken = default)
    {
        var video = await GetVideoAsync(url, cancellationToken);
        return new YoutubePreview(
            video.Title,
            video.Author?.ChannelTitle,
            video.Duration is { } d ? (int)d.TotalSeconds : null,
            HighestThumbnailUrl(video));
    }

    public async Task<ExtractedYoutubeAudio> ExtractAsync(string url, CancellationToken cancellationToken = default)
    {
        var video = await GetVideoAsync(url, cancellationToken);

        StreamManifest manifest;
        try
        {
            manifest = await _youtube.Videos.Streams.GetManifestAsync(video.Id, cancellationToken);
        }
        catch (VideoUnavailableException ex)
        {
            _logger.LogWarning(ex, "Video unavailable for streaming: {Url}", url);
            throw new AppException(
                "This video can't be imported — it's likely age-restricted (YouTube requires sign-in), private, region-blocked, or removed. Try a different public video.", 422);
        }
        catch (VideoUnplayableException ex)
        {
            _logger.LogWarning(ex, "Video unplayable: {Url}", url);
            throw new AppException(
                "This video can't be played/downloaded — it may be members-only, a paid title, or restricted. Try a different video.", 422);
        }
        catch (RequestLimitExceededException ex)
        {
            _logger.LogWarning(ex, "YouTube rate limit for {Url}", url);
            throw new AppException("YouTube is rate-limiting requests right now. Please try again in a few minutes.", 429);
        }
        catch (Exception ex) when (ex is not AppException and not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to read stream manifest for {Url}", url);
            throw new AppException("Couldn't read the audio streams for this video. It may be age-restricted or unavailable.", 422);
        }

        // Prefer an mp4/AAC audio stream (saved as .m4a — plays natively in browsers); fall back to
        // the best audio-only stream of any container if none is available.
        var mp4 = manifest.GetAudioOnlyStreams().Where(s => s.Container == Container.Mp4).ToList();
        var pool = mp4.Count > 0 ? mp4 : manifest.GetAudioOnlyStreams().ToList();
        if (pool.Count == 0)
            throw new AppException("No audio stream is available for this video.", 422);

        var audio = pool.GetWithHighestBitrate();
        var isMp4 = audio.Container == Container.Mp4;
        var ext = isMp4 ? ".m4a" : "." + audio.Container.Name;
        var contentType = isMp4 ? "audio/mp4" : "audio/" + audio.Container.Name;

        var audioPath = Path.Combine(Path.GetTempPath(), $"yt_{Guid.NewGuid():N}{ext}");
        try
        {
            await _youtube.Videos.Streams.DownloadAsync(audio, audioPath, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDelete(audioPath);
            _logger.LogWarning(ex, "Failed to download audio for {Url}", url);
            throw new AppException("Failed to download the audio from YouTube.", 422);
        }

        // Best-effort thumbnail download; a missing cover must not fail the import.
        string? thumbPath = null, thumbName = null, thumbType = null;
        var thumbUrl = HighestThumbnailUrl(video);
        if (!string.IsNullOrEmpty(thumbUrl))
        {
            try
            {
                thumbPath = Path.Combine(Path.GetTempPath(), $"yt_{Guid.NewGuid():N}.jpg");
                await using var source = await Http.GetStreamAsync(thumbUrl, cancellationToken);
                await using (var dest = File.Create(thumbPath))
                    await source.CopyToAsync(dest, cancellationToken);
                thumbName = Path.GetFileName(thumbPath);
                thumbType = "image/jpeg";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Thumbnail download failed for {Url}", url);
                TryDelete(thumbPath);
                thumbPath = null;
            }
        }

        return new ExtractedYoutubeAudio
        {
            Title = video.Title,
            Author = video.Author?.ChannelTitle,
            DurationInSeconds = video.Duration is { } d ? (int)d.TotalSeconds : null,
            AudioFilePath = audioPath,
            AudioFileName = $"{video.Id}{ext}",
            AudioContentType = contentType,
            ThumbnailFilePath = thumbPath,
            ThumbnailFileName = thumbName,
            ThumbnailContentType = thumbType
        };
    }

    private async Task<Video> GetVideoAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await _youtube.Videos.GetAsync(url, cancellationToken);
        }
        catch (ArgumentException)
        {
            throw new AppException("That doesn't look like a valid YouTube URL.", 400);
        }
        catch (VideoUnavailableException ex)
        {
            _logger.LogWarning(ex, "Video unavailable: {Url}", url);
            throw new AppException(
                "This video isn't available — it may be private, deleted, region-blocked, or age-restricted (sign-in required). Try a different public video.", 422);
        }
        catch (HttpRequestException ex)
        {
            // Reaching youtube.com failed at the network layer (e.g. a firewall/proxy blocking it).
            _logger.LogWarning(ex, "Couldn't reach YouTube for {Url}", url);
            throw new AppException("Couldn't reach YouTube. The server's network may be blocking youtube.com (check firewall/proxy).", 502);
        }
        catch (Exception ex) when (ex is not AppException and not OperationCanceledException)
        {
            _logger.LogWarning(ex, "YouTube lookup failed for {Url}", url);
            throw new AppException("Couldn't read that YouTube video. It may be private, age-restricted, or removed.", 422);
        }
    }

    private static string? HighestThumbnailUrl(Video video)
        => video.Thumbnails.Count > 0 ? video.Thumbnails.GetWithHighestResolution().Url : null;

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }
}
