using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Admin;
using MusicWebsite.Application.DTOs.Playlists;
using MusicWebsite.Application.Interfaces.Persistence;
using MusicWebsite.Application.Interfaces.Services;

namespace MusicWebsite.Application.Services;

public class AdminService : IAdminService
{
    private readonly IAccountRepository _accounts;
    private readonly IPlaylistRepository _playlists;

    public AdminService(IAccountRepository accounts, IPlaylistRepository playlists)
    {
        _accounts = accounts;
        _playlists = playlists;
    }

    public Task<IEnumerable<AdminUserDto>> GetUsersAsync() => _accounts.GetAllWithRoleAsync();

    public async Task<MessageResponse> SetRoleAsync(Guid targetAccountId, string role, Guid callerAccountId)
    {
        if (!Roles.Assignable.Contains(role))
            throw new AppException("Role must be User or Admin.", 400);
        if (targetAccountId == callerAccountId)
            throw new AppException("You can't change your own role.", 400);

        var target = await _accounts.GetByIdAsync(targetAccountId)
            ?? throw new AppException("Account not found.", 404);
        if (string.Equals(target.Role, Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase))
            throw new AppException("SuperAdmin accounts can only be changed directly in the database.", 403);

        await _accounts.SetRoleAsync(targetAccountId, role);
        return new MessageResponse { Success = true, Message = $"Role updated to {role}." };
    }

    public async Task<MessageResponse> DeleteAccountAsync(Guid targetAccountId, Guid callerAccountId)
    {
        if (targetAccountId == callerAccountId)
            throw new AppException("You can't delete your own account here.", 400);

        var target = await _accounts.GetByIdAsync(targetAccountId)
            ?? throw new AppException("Account not found.", 404);
        if (string.Equals(target.Role, Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase))
            throw new AppException("SuperAdmin accounts can only be deleted directly in the database.", 403);

        return await _accounts.CascadeDeleteAsync(targetAccountId);
    }

    public Task<IEnumerable<PlaylistDto>> GetUserPlaylistsAsync(Guid accountId)
        => _playlists.GetByAccountIdAsync(accountId);
}
