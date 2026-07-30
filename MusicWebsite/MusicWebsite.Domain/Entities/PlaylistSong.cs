namespace MusicWebsite.Domain.Entities;

public class PlaylistSong
{
    public Guid PlaylistSongId { get; set; }
    public Guid PlaylistId { get; set; }
    public Guid SongId { get; set; }
    public DateTime Created { get; set; }
}
