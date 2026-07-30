using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Playlists;
using MusicWebsite.Application.Interfaces.Persistence;
using MusicWebsite.Domain.Entities;

namespace MusicWebsite.Infrastructure.Persistence.Repositories;

public class PlaylistSongRepository : RepositoryBase, IPlaylistSongRepository
{
    public PlaylistSongRepository(IDbConnectionFactory factory) : base(factory) { }

    public Task<PlaylistSong> AddAsync(Guid playlistSongId, Guid playlistId, Guid songId)
        => QueryFirstAsync<PlaylistSong>(StoredProcedures.PlaylistSongAdd,
            new { PlaylistSongId = playlistSongId, PlaylistId = playlistId, SongId = songId });

    public Task<MessageResponse> RemoveAsync(Guid playlistId, Guid songId)
        => QueryFirstAsync<MessageResponse>(StoredProcedures.PlaylistSongRemove,
            new { PlaylistId = playlistId, SongId = songId });

    public Task<IEnumerable<PlaylistSongDto>> GetByPlaylistIdAsync(Guid playlistId)
        => QueryAsync<PlaylistSongDto>(StoredProcedures.PlaylistSongGetByPlaylistId,
            new { PlaylistId = playlistId });
}
