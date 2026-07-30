using System.ComponentModel.DataAnnotations;

namespace MusicWebsite.Application.DTOs.Songs;

public class SongDto
{
    public Guid SongId { get; set; }
    public string SongName { get; set; } = string.Empty;
    public string SongUrl { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int? DurationInSeconds { get; set; }
    /// <summary>Total community priority (sum of all up/down votes). Higher ranks first in search.</summary>
    public int Priority { get; set; }
    /// <summary>The current user's own vote for this song: 1 (up), -1 (down), or 0 (none).</summary>
    public int MyVote { get; set; }
    /// <summary>The account that uploaded this song (for delete-permission checks on the client).</summary>
    public Guid? UploadedByAccountId { get; set; }
    public DateTime? Created { get; set; }
    public DateTime? Updated { get; set; }

    /// <summary>Performer. Filled in automatically on upload; editable afterwards.</summary>
    public string? Artist { get; set; }
    public Guid? CategoryId { get; set; }
    /// <summary>Category name, e.g. "Bollywood". Created automatically the first time it's used.</summary>
    public string? Category { get; set; }

    /// <summary>Plain-text lyrics. Only populated by the single-song endpoint, not by lists.</summary>
    public string? Lyrics { get; set; }
    /// <summary>LRC lyrics with timestamps, so the player can scroll them in time with the audio.</summary>
    public string? LyricsSynced { get; set; }
}

/// <summary>A category with how many songs currently sit in it.</summary>
public class CategoryDto
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TotalSongs { get; set; }
}

/// <summary>Sets the current user's single vote for a song. Value is normalised to 1 / 0 / -1.</summary>
public class SongVoteRequest
{
    public int Value { get; set; }
}

public class CreateSongRequest
{
    [Required, MaxLength(250)]
    public string SongName { get; set; } = string.Empty;

    // Until file upload is wired, the client supplies the URL directly.
    [Required]
    public string SongUrl { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    [Range(0, int.MaxValue)]
    public int? DurationInSeconds { get; set; }

    [Range(0, int.MaxValue)]
    public int Priority { get; set; } = 0;
}

/// <summary>Metadata that accompanies a file upload (the files themselves travel separately).</summary>
public class UploadSongRequest
{
    [Required, MaxLength(250)]
    public string SongName { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int? DurationInSeconds { get; set; }

    [Range(0, int.MaxValue)]
    public int Priority { get; set; } = 0;

    /// <summary>Optional. Left blank, the artist is read from the file's own tags.</summary>
    [MaxLength(200)]
    public string? Artist { get; set; }

    /// <summary>
    /// Optional. Left blank, the genre tag is used; failing that the song lands in
    /// "Uncategorized". Any name is accepted — unknown ones are created on the spot.
    /// </summary>
    [MaxLength(100)]
    public string? Category { get; set; }
}

/// <summary>Just a YouTube URL — used to fetch a preview before importing.</summary>
public class YoutubeUrlRequest
{
    [Required]
    public string Url { get; set; } = string.Empty;
}

/// <summary>Import a song by extracting its audio from a YouTube link.</summary>
public class ImportYoutubeRequest
{
    [Required]
    public string Url { get; set; } = string.Empty;

    /// <summary>Optional override for the song name; defaults to the video title.</summary>
    [MaxLength(250)]
    public string? SongName { get; set; }

    [Range(0, int.MaxValue)]
    public int Priority { get; set; } = 0;

    /// <summary>Optional override; defaults to whatever YouTube reports for the track.</summary>
    [MaxLength(200)]
    public string? Artist { get; set; }

    /// <summary>Optional override; defaults to the video's genre, else "Uncategorized".</summary>
    [MaxLength(100)]
    public string? Category { get; set; }
}

/// <summary>Preview metadata returned before a YouTube import is committed.</summary>
public class YoutubePreviewDto
{
    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public int? DurationInSeconds { get; set; }
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// What the import will actually save, after cleaning the title and working out the artist.
    /// Shown on the confirm screen so the uploader can correct it before committing rather than
    /// discovering afterwards that the song was filed as "Saregama Music".
    /// </summary>
    public string? SuggestedSongName { get; set; }
    public string? SuggestedArtist { get; set; }
    public string? SuggestedCategory { get; set; }
}

public class UpdateSongRequest
{
    [Required, MaxLength(250)]
    public string SongName { get; set; } = string.Empty;

    [Required]
    public string SongUrl { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    [Range(0, int.MaxValue)]
    public int? DurationInSeconds { get; set; }

    [Range(0, int.MaxValue)]
    public int Priority { get; set; } = 0;

    [MaxLength(200)]
    public string? Artist { get; set; }

    /// <summary>Category name. Unknown names are created; blank leaves the category unchanged.</summary>
    [MaxLength(100)]
    public string? Category { get; set; }
}
