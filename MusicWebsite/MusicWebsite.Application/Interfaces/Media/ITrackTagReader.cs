using MusicWebsite.Application.Models;

namespace MusicWebsite.Application.Interfaces.Media;

/// <summary>
/// Reads embedded tags (ID3, Vorbis, MP4 atoms) out of an audio file. This is the most reliable
/// metadata source there is — the artist and title were written by whoever made the file, so
/// nothing is guessed. Always best-effort: a file with no tags returns
/// <see cref="TrackMetadata.Empty"/> rather than failing the upload.
/// </summary>
public interface ITrackTagReader
{
    /// <summary>
    /// Reads what tags the file carries. The stream is rewound to its original position before
    /// returning, so the same bytes can still be hashed and uploaded.
    /// </summary>
    TrackMetadata Read(Stream audio, string fileName);
}
