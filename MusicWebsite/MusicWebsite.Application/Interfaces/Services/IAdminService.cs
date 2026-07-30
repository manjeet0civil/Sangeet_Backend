using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Admin;
using MusicWebsite.Application.DTOs.Playlists;

namespace MusicWebsite.Application.Interfaces.Services;

/// <summary>SuperAdmin-only operations: manage accounts, roles, and view/remove any user's data.</summary>
public interface IAdminService
{
    Task<IEnumerable<AdminUserDto>> GetUsersAsync();
    Task<MessageResponse> SetRoleAsync(Guid targetAccountId, string role, Guid callerAccountId);
    Task<MessageResponse> DeleteAccountAsync(Guid targetAccountId, Guid callerAccountId);
    Task<IEnumerable<PlaylistDto>> GetUserPlaylistsAsync(Guid accountId);
}
