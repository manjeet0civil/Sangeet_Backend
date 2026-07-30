using System.Text.RegularExpressions;
using MusicWebsite.Application.Models;

namespace MusicWebsite.Application.Common;

/// <summary>
/// Turns a messy YouTube title into a clean track title and, where it can, an artist.
///
/// This exists because lyrics lookups fail on raw titles. Given
/// <c>"Kahi Door Jab with Lyrics | Anand | Rajesh Khanna | Saregama Music"</c>, a lyrics database
/// is asked for the track "Kahi Door Jab with Lyrics | Anand | ..." by the artist "Saregama Music"
/// and unsurprisingly returns nothing. Cleaned to "Kahi Door Jab", it matches.
///
/// Everything here is deterministic string work — no network, no API key, no cost, and no chance
/// of inventing an artist who doesn't exist. It is deliberately conservative: when a title is
/// ambiguous it keeps more text rather than guessing wrong, because a slightly noisy title still
/// displays fine, whereas a wrong artist corrupts duplicate detection.
/// </summary>
public static partial class TrackTitleParser
{
    /// <summary>
    /// Promotional phrases that are never part of a song's name. Matched case-insensitively as
    /// whole phrases so a song genuinely called "Video Games" keeps its title.
    /// </summary>
    private static readonly string[] NoisePhrases =
    {
        "official music video", "official lyric video", "official lyrics video", "official video song",
        "official video", "official audio", "official song", "official trailer",
        "full video song", "full audio song", "full video", "full audio", "full song",
        "lyric video", "lyrics video", "with lyrics", "with lyric", "lyrical video", "lyrical",
        "video song", "audio song", "song video",
        "remastered", "reprise version", "cover version",
        "hd video", "4k video", "8k video", "hq audio",
        "audio jukebox", "jukebox"
    };

    /// <summary>
    /// Parses a raw YouTube title (and optionally the uploading channel) into cleaner fields.
    /// Returns <see cref="TrackMetadata.Empty"/>'s style of "unknown" — nulls — for anything it
    /// can't work out, so the caller can fall back to another source.
    /// </summary>
    public static TrackMetadata Parse(string? rawTitle, string? uploader = null)
    {
        var artist = ArtistFromChannel(uploader);
        var title = Clean(TakeFirstSegment(rawTitle));

        // Strip the artist's own name off the title when we already know it independently:
        // "Arijit Singh - Tum Hi Ho" and "Tum Hi Ho - Arijit Singh" both reduce to "Tum Hi Ho".
        // Safe precisely because the artist came from somewhere else — we are removing a known
        // duplicate, not deciding which half is which.
        if (artist is not null && title is not null)
            title = StripKnownArtist(title, artist) ?? title;

        // Note what is deliberately NOT done here: splitting "A - B" into artist and title when
        // nothing else told us the artist. That split is unresolvable from the string alone —
        // "Arijit Singh - Tum Hi Ho" and "Tum Hi Ho - Aashiqui 2" are the same shape, and in the
        // second one the left side is the song and the right is the film. Guessing gets it
        // backwards roughly as often as it gets it right, and a fabricated artist is the most
        // expensive mistake available: it is written to the row, it blocks the real song from
        // being uploaded later by the title+artist duplicate check, and it sends the lyrics
        // lookup after a performer who does not exist. Leaving the artist null costs a slightly
        // noisy display name that the uploader can fix on the confirm screen.
        return new TrackMetadata(Title: title, Artist: artist);
    }

    /// <summary>
    /// Pulls the artist out of a channel name. YouTube's auto-generated music channels are named
    /// "&lt;Artist&gt; - Topic", which is the single most reliable artist signal available — those
    /// channels are created by YouTube from the label's own metadata, not typed by an uploader.
    /// Ordinary channel names are rejected: "T-Series" and "Saregama Music" are labels, not artists.
    /// </summary>
    public static string? ArtistFromChannel(string? uploader)
    {
        if (string.IsNullOrWhiteSpace(uploader)) return null;

        var name = uploader.Trim();
        const string topic = " - Topic";
        if (!name.EndsWith(topic, StringComparison.OrdinalIgnoreCase)) return null;

        var artist = name[..^topic.Length].Trim();
        return string.IsNullOrWhiteSpace(artist) || Equivalent(artist, "Various Artists") ? null : artist;
    }

    /// <summary>
    /// Everything before the first pipe. YouTube titles pile on context after it —
    /// film, cast, label, year — and none of it belongs in the song's name.
    /// </summary>
    private static string? TakeFirstSegment(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var pipe = raw.IndexOf('|');
        var head = pipe > 0 ? raw[..pipe] : raw;
        return string.IsNullOrWhiteSpace(head) ? raw : head;
    }

    /// <summary>Strips bracketed asides, promo phrases, and leftover punctuation.</summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = BracketedRegex().Replace(value, " ");

        foreach (var phrase in NoisePhrases)
            text = Regex.Replace(text, Regex.Escape(phrase), " ", RegexOptions.IgnoreCase);

        // Bare resolution/quality markers, only as standalone words.
        text = QualityMarkerRegex().Replace(text, " ");

        text = WhitespaceRegex().Replace(text, " ").Trim();
        text = text.Trim(' ', '-', '–', '—', '|', ':', ',', '.', '"', '\'');
        text = WhitespaceRegex().Replace(text, " ").Trim();

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// Removes an already-known artist name from either end of a dash-separated title, returning
    /// null when the title doesn't have that shape. Only ever called with an artist established
    /// from another source, so it never has to decide which half is which.
    /// </summary>
    private static string? StripKnownArtist(string title, string artist)
    {
        var match = DashRegex().Match(title);
        if (!match.Success) return null;

        var left = title[..match.Index].Trim();
        var right = title[(match.Index + match.Length)..].Trim();
        if (left.Length == 0 || right.Length == 0) return null;

        if (Equivalent(left, artist)) return right;
        if (Equivalent(right, artist)) return left;
        return null;
    }

    private static bool Equivalent(string a, string b)
        => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"[\(\[\{][^\)\]\}]*[\)\]\}]")]
    private static partial Regex BracketedRegex();

    [GeneratedRegex(@"\b(?:hd|full\s*hd|4k|8k|1080p|720p|hq)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QualityMarkerRegex();

    [GeneratedRegex(@"\s+[-–—]\s+")]
    private static partial Regex DashRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceRegex();
}
