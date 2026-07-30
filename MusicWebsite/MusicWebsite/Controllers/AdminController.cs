using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Admin;
using MusicWebsite.Application.DTOs.Playlists;
using MusicWebsite.Application.Interfaces.Services;
using MusicWebsite.Extensions;

namespace MusicWebsite.Controllers;

/// <summary>SuperAdmin-only console: manage accounts, roles, and view/remove any user's data.</summary>
[ApiController]
[Authorize(Roles = Roles.SuperAdmin)]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _admin;

    public AdminController(IAdminService admin) => _admin = admin;

    /// <summary>List every account with its role and profile.</summary>
    [HttpGet("users")]
    public async Task<ActionResult<ApiResponse<IEnumerable<AdminUserDto>>>> GetUsers()
    {
        var users = await _admin.GetUsersAsync();
        return Ok(ApiResponse<IEnumerable<AdminUserDto>>.Ok(users));
    }

    /// <summary>Set an account's role (User or Admin only — SuperAdmin is DB-only).</summary>
    [HttpPut("users/{accountId:guid}/role")]
    public async Task<ActionResult<ApiResponse<MessageResponse>>> SetRole(Guid accountId, [FromBody] SetRoleRequest request)
    {
        var result = await _admin.SetRoleAsync(accountId, request.Role, User.GetAccountId());
        return Ok(ApiResponse<MessageResponse>.Ok(result, result.Message));
    }

    /// <summary>Permanently delete an account and all its data (playlists, votes, profile).</summary>
    [HttpDelete("users/{accountId:guid}")]
    public async Task<ActionResult<ApiResponse<MessageResponse>>> DeleteAccount(Guid accountId)
    {
        var result = await _admin.DeleteAccountAsync(accountId, User.GetAccountId());
        return Ok(ApiResponse<MessageResponse>.Ok(result, result.Message));
    }

    /// <summary>View any user's playlists.</summary>
    [HttpGet("users/{accountId:guid}/playlists")]
    public async Task<ActionResult<ApiResponse<IEnumerable<PlaylistDto>>>> GetUserPlaylists(Guid accountId)
    {
        var playlists = await _admin.GetUserPlaylistsAsync(accountId);
        return Ok(ApiResponse<IEnumerable<PlaylistDto>>.Ok(playlists));
    }
}
