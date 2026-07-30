namespace MusicWebsite.Domain.Entities;

public class Playlist
{
    public Guid PlaylistId { get; set; }
    public Guid AccountId { get; set; }
    public string PlaylistName { get; set; } = string.Empty;
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}
