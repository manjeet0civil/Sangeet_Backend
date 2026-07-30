using Microsoft.Extensions.Logging;
using MusicWebsite.Application.Interfaces.Media;
using MusicWebsite.Application.Models;

namespace MusicWebsite.Infrastructure.Media;

/// <summary>
/// Reads embedded tags with TagLib#, which understands ID3v1/ID3v2 (MP3), MP4 atoms (M4A/AAC),
/// Vorbis comments (OGG/FLAC) and WAV — the full set of formats this app accepts.
///
/// This is the highest-quality metadata source available for a direct upload: the artist, title
/// and genre were written into the file by whoever produced it, so nothing is inferred. It also
/// yields the real duration, which is why uploaded songs previously showed no length — nothing
/// was measuring it.
///
/// Every failure is swallowed. A corrupt or untagged file must still upload; missing metadata is
/// an inconvenience, a rejected upload is a broken feature.
/// </summary>
public class TagLibTrackTagReader : ITrackTagReader
{
    private readonly ILogger<TagLibTrackTagReader> _logger;

    public TagLibTrackTagReader(ILogger<TagLibTrackTagReader> logger) => _logger = logger;

    public TrackMetadata Read(Stream audio, string fileName)
    {
        // TagLib seeks all over the file to find tag blocks at both ends, so a forward-only
        // stream can't be read. Callers hand us a buffered/seekable stream, but guard anyway.
        if (!audio.CanSeek) return TrackMetadata.Empty;

        var origin = audio.Position;
        try
        {
            audio.Position = 0;

            using var file = TagLib.File.Create(new StreamFileAbstraction(fileName, audio));
            var tag = file.Tag;

            var duration = file.Properties?.Duration ?? TimeSpan.Zero;

            return new TrackMetadata(
                Title: Clean(tag.Title),
                // FirstPerformer is the track artist; AlbumArtist is the fallback for
                // compilations where the per-track performer was left blank.
                Artist: Clean(tag.FirstPerformer) ?? Clean(tag.FirstAlbumArtist),
                Album: Clean(tag.Album),
                Category: Clean(tag.FirstGenre),
                DurationInSeconds: duration > TimeSpan.Zero ? (int)Math.Round(duration.TotalSeconds) : null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No readable tags in '{FileName}'", fileName);
            return TrackMetadata.Empty;
        }
        finally
        {
            // Hand the stream back exactly as we found it — it still has to be hashed and uploaded.
            try { audio.Position = origin; } catch { /* nothing more we can do */ }
        }
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Lets TagLib# read from an in-memory stream instead of a path, so nothing has to be written
    /// to disk. CloseStream is intentionally a no-op: the stream belongs to the caller, who still
    /// needs it afterwards.
    /// </summary>
    private sealed class StreamFileAbstraction : TagLib.File.IFileAbstraction
    {
        public StreamFileAbstraction(string name, Stream stream)
        {
            Name = name;
            ReadStream = stream;
            WriteStream = stream;
        }

        public string Name { get; }
        public Stream ReadStream { get; }
        public Stream WriteStream { get; }

        public void CloseStream(Stream stream) { /* owned by the caller */ }
    }
}
