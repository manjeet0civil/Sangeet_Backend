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
}

/// <summary>Preview metadata returned before a YouTube import is committed.</summary>
public class YoutubePreviewDto
{
    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public int? DurationInSeconds { get; set; }
    public string? ThumbnailUrl { get; set; }
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
}
