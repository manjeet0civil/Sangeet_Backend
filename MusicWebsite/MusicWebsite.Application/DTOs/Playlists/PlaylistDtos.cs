using System.ComponentModel.DataAnnotations;

namespace MusicWebsite.Application.DTOs.Playlists;

public class PlaylistDto
{
    public Guid PlaylistId { get; set; }
    public Guid AccountId { get; set; }
    public string PlaylistName { get; set; } = string.Empty;
    public int TotalSongs { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}

public class CreatePlaylistRequest
{
    [Required, MaxLength(200)]
    public string PlaylistName { get; set; } = string.Empty;
}

public class UpdatePlaylistRequest
{
    [Required, MaxLength(200)]
    public string PlaylistName { get; set; } = string.Empty;
}

/// <summary>A song as it appears inside a playlist (join of Songs + PlaylistSongs).</summary>
public class PlaylistSongDto
{
    public Guid PlaylistSongId { get; set; }
    public Guid SongId { get; set; }
    public string SongName { get; set; } = string.Empty;
    public string SongUrl { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int? DurationInSeconds { get; set; }
    public int Priority { get; set; }
    public DateTime AddedOn { get; set; }
}
