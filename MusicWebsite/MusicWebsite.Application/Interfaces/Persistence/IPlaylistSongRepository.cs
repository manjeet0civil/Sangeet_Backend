using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Playlists;
using MusicWebsite.Domain.Entities;

namespace MusicWebsite.Application.Interfaces.Persistence;

public interface IPlaylistSongRepository
{
    Task<PlaylistSong> AddAsync(Guid playlistSongId, Guid playlistId, Guid songId);
    Task<MessageResponse> RemoveAsync(Guid playlistId, Guid songId);
    Task<IEnumerable<PlaylistSongDto>> GetByPlaylistIdAsync(Guid playlistId);
}
