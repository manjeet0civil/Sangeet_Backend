using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Playlists;
using MusicWebsite.Domain.Entities;

namespace MusicWebsite.Application.Interfaces.Persistence;

public interface IPlaylistRepository
{
    Task<Playlist> InsertAsync(Guid playlistId, Guid accountId, string playlistName);
    Task<Playlist> UpdateAsync(Guid playlistId, string playlistName);
    Task<PlaylistDto?> GetByIdAsync(Guid playlistId);
    Task<IEnumerable<PlaylistDto>> GetByAccountIdAsync(Guid accountId);
    Task<MessageResponse> DeleteAsync(Guid playlistId);
}
