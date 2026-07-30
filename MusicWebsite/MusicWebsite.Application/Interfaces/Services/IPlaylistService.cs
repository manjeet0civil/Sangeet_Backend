using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Playlists;

namespace MusicWebsite.Application.Interfaces.Services;

public interface IPlaylistService
{
    Task<IEnumerable<PlaylistDto>> GetMyPlaylistsAsync(Guid accountId);
    Task<PlaylistDto> GetByIdAsync(Guid playlistId, Guid accountId);
    Task<PlaylistDto> CreateAsync(Guid accountId, CreatePlaylistRequest request);
    Task<PlaylistDto> UpdateAsync(Guid playlistId, Guid accountId, UpdatePlaylistRequest request);
    Task<MessageResponse> DeleteAsync(Guid playlistId, Guid accountId);

    Task<IEnumerable<PlaylistSongDto>> GetSongsAsync(Guid playlistId, Guid accountId);
    Task<PlaylistSongDto> AddSongAsync(Guid playlistId, Guid accountId, Guid songId);
    Task<MessageResponse> RemoveSongAsync(Guid playlistId, Guid accountId, Guid songId);
}
