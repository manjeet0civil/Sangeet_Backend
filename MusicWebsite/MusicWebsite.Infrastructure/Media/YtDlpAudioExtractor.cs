using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicWebsite.Application.Common;
using MusicWebsite.Application.Interfaces.Media;
using MusicWebsite.Application.Models;

namespace MusicWebsite.Infrastructure.Media;

/// <summary>
/// Extracts audio using the <c>yt-dlp</c> command-line tool — far more robust than the pure-.NET
/// scraper, and kept up to date with YouTube by its maintainers. Downloads the best audio-only
/// stream directly (usually .m4a, so no ffmpeg is required) to a temp folder, then hands it to the
/// caller for upload; the folder is deleted on dispose.
/// </summary>
public partial class YtDlpAudioExtractor : IYoutubeAudioExtractor
{
    private static readonly HttpClient Http = new();
    private readonly YoutubeSettings _settings;
    private readonly ILogger<YtDlpAudioExtractor> _logger;

    public YtDlpAudioExtractor(IOptions<YoutubeSettings> settings, ILogger<YtDlpAudioExtractor> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public string? TryGetVideoId(string url)
    {
        var m = VideoIdRegex().Match(url ?? string.Empty);
        return m.Success ? m.Groups["id"].Value : null;
    }

    public async Task<YoutubePreview> GetPreviewAsync(string url, CancellationToken cancellationToken = default)
    {
        using var doc = await DumpJsonAsync(url, cancellationToken);
        var root = doc.RootElement;
        return new YoutubePreview(
            GetString(root, "title") ?? "Untitled",
            GetString(root, "uploader") ?? GetString(root, "channel"),
            GetDurationSeconds(root),
            GetString(root, "thumbnail"));
    }

    public async Task<ExtractedYoutubeAudio> ExtractAsync(string url, CancellationToken cancellationToken = default)
    {
        // 1. Metadata (title/duration/thumbnail) — fast, no download.
        using var doc = await DumpJsonAsync(url, cancellationToken);
        var root = doc.RootElement;
        var title = GetString(root, "title") ?? "Untitled";
        var author = GetString(root, "uploader") ?? GetString(root, "channel");
        var duration = GetDurationSeconds(root);
        var thumbUrl = GetString(root, "thumbnail");
        var videoId = GetString(root, "id") ?? TryGetVideoId(url) ?? Guid.NewGuid().ToString("N");

        // 2. Download the best audio-only stream into a dedicated temp folder.
        var dir = Path.Combine(Path.GetTempPath(), "ytdlp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await RunYtDlpAsync(new[]
            {
                "-f", "bestaudio[ext=m4a]/bestaudio",
                "--no-playlist", "--no-part", "--no-progress",
                "-o", Path.Combine(dir, "audio.%(ext)s"),
                url
            }, cancellationToken);

            var audioPath = Directory.GetFiles(dir, "audio.*").FirstOrDefault()
                ?? throw new AppException("yt-dlp did not produce an audio file for this video.", 422);

            var ext = Path.GetExtension(audioPath).ToLowerInvariant();
            var contentType = ext switch
            {
                ".m4a" or ".mp4" or ".aac" => "audio/mp4",
                ".webm" => "audio/webm",
                ".opus" => "audio/ogg",
                ".mp3" => "audio/mpeg",
                _ => "application/octet-stream"
            };

            // 3. Best-effort thumbnail download.
            string? thumbPath = null, thumbName = null, thumbType = null;
            if (!string.IsNullOrEmpty(thumbUrl))
            {
                try
                {
                    thumbPath = Path.Combine(dir, "cover.jpg");
                    await using var src = await Http.GetStreamAsync(thumbUrl, cancellationToken);
                    await using (var dest = File.Create(thumbPath))
                        await src.CopyToAsync(dest, cancellationToken);
                    thumbName = "cover.jpg";
                    thumbType = "image/jpeg";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Thumbnail download failed for {Url}", url);
                    thumbPath = null;
                }
            }

            return new ExtractedYoutubeAudio
            {
                Title = title,
                Author = author,
                DurationInSeconds = duration,
                AudioFilePath = audioPath,
                AudioFileName = $"{videoId}{ext}",
                AudioContentType = contentType,
                ThumbnailFilePath = thumbPath,
                ThumbnailFileName = thumbName,
                ThumbnailContentType = thumbType,
                TempDirectory = dir
            };
        }
        catch
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
            throw;
        }
    }

    // ---- yt-dlp process plumbing -------------------------------------------------

    private async Task<JsonDocument> DumpJsonAsync(string url, CancellationToken cancellationToken)
    {
        var stdout = await RunYtDlpAsync(new[]
        {
            "--dump-single-json", "--no-playlist", "--skip-download", url
        }, cancellationToken);

        try
        {
            return JsonDocument.Parse(stdout);
        }
        catch (JsonException)
        {
            throw new AppException("Couldn't read this video's details from yt-dlp.", 422);
        }
    }

    /// <summary>Runs yt-dlp with the given args, returns stdout, throws a friendly AppException on failure.</summary>
    private async Task<string> RunYtDlpAsync(string[] args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _settings.YtDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Youtube:UseProxy is the master switch for the whole feature. It is off by default, and
        // when off we refuse immediately rather than attempt a direct call: YouTube blocks
        // datacenter IPs, so an unproxied request from the server fails on every video with
        // "Sign in to confirm you're not a bot". Failing fast with an honest message beats making
        // someone wait ~30s for a download that cannot succeed.
        if (!_settings.UseProxy)
        {
            throw new AppException(
                "YouTube import is turned off on this server. Upload the audio file directly instead.",
                503);
        }

        if (string.IsNullOrWhiteSpace(_settings.ProxyUrl))
        {
            _logger.LogError("Youtube:UseProxy is true but Youtube:ProxyUrl is empty.");
            throw new AppException(
                "YouTube import is misconfigured on this server. Please contact the administrator.",
                500);
        }

        // Route ONLY yt-dlp through the proxy. Nothing else in the application uses it — the
        // database, B2 and every API call keep the server's own connection.
        //
        // Added to ArgumentList rather than to `args` on purpose: the failure log below prints
        // `args`, and the proxy URL carries a username and password that must stay out of logs.
        psi.ArgumentList.Add("--proxy");
        psi.ArgumentList.Add(_settings.ProxyUrl);

        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start yt-dlp at '{Path}'", _settings.YtDlpPath);
            throw new AppException($"yt-dlp is not available on the server (looked for '{_settings.YtDlpPath}'). Check the Youtube:YtDlpPath setting.", 500);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new AppException("The YouTube download took too long and was cancelled.", 504);
        }

        if (process.ExitCode != 0)
        {
            var err = stderr.ToString();
            _logger.LogWarning("yt-dlp failed ({Code}) for args [{Args}]: {Err}", process.ExitCode, string.Join(' ', args), err);
            throw new AppException(MapError(err), 422);
        }

        return stdout.ToString();
    }

    /// <summary>Turns yt-dlp's stderr into a clear, user-facing message.</summary>
    private static string MapError(string stderr)
    {
        var e = stderr.ToLowerInvariant();
        if (e.Contains("sign in to confirm your age") || e.Contains("age-restricted") || e.Contains("age restricted"))
            return "This video is age-restricted — YouTube requires sign-in, so it can't be imported.";
        if (e.Contains("private video"))
            return "This is a private video and can't be imported.";
        if (e.Contains("video unavailable") || e.Contains("removed") || e.Contains("no longer available"))
            return "This video isn't available (it may be removed or unavailable).";
        if (e.Contains("not available in your country") || e.Contains("geo"))
            return "This video is blocked in the server's region and can't be imported.";
        if (e.Contains("members-only") || e.Contains("join this channel"))
            return "This is a members-only video and can't be imported.";
        if (e.Contains("sign in to confirm you're not a bot") || e.Contains("confirm you’re not a bot"))
            return "YouTube is asking the server to verify it's not a bot. Try again shortly, or import a different video.";
        return "Couldn't download the audio from this YouTube video. Try a different one, or upload the file directly.";
    }

    // ---- JSON helpers ------------------------------------------------------------

    private static string? GetString(JsonElement root, string prop)
        => root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetDurationSeconds(JsonElement root)
    {
        if (root.TryGetProperty("duration", out var v) && v.ValueKind == JsonValueKind.Number)
            return (int)Math.Round(v.GetDouble());
        return null;
    }

    [GeneratedRegex(@"(?:v=|youtu\.be/|/shorts/|/embed/)(?<id>[A-Za-z0-9_-]{11})")]
    private static partial Regex VideoIdRegex();
}
