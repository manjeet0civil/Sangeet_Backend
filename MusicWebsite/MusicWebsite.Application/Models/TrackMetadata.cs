namespace MusicWebsite.Application.Models;

/// <summary>
/// What we managed to work out about a track before saving it. Every field is optional —
/// a source that knows nothing returns <see cref="Empty"/> and the next source gets a turn.
/// </summary>
public sealed record TrackMetadata(
    string? Title = null,
    string? Artist = null,
    string? Album = null,
    string? Category = null,
    int? DurationInSeconds = null)
{
    public static readonly TrackMetadata Empty = new();

    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
    public bool HasArtist => !string.IsNullOrWhiteSpace(Artist);

    /// <summary>
    /// Fills this record's blank fields from <paramref name="fallback"/>. Used to layer sources
    /// cheapest-and-most-trustworthy first: ID3 tags, then YouTube's own fields, then the
    /// title parser, then (optionally) AI.
    /// </summary>
    public TrackMetadata FillFrom(TrackMetadata fallback) => new(
        Pick(Title, fallback.Title),
        Pick(Artist, fallback.Artist),
        Pick(Album, fallback.Album),
        Pick(Category, fallback.Category),
        DurationInSeconds ?? fallback.DurationInSeconds);

    private static string? Pick(string? preferred, string? fallback)
        => string.IsNullOrWhiteSpace(preferred) ? Blank(fallback) : preferred.Trim();

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
