using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Playlists;
using MusicWebsite.Application.Interfaces.Services;
using MusicWebsite.Extensions;

namespace MusicWebsite.Controllers;

[ApiController]
[Authorize]
[Route("api/playlists")]
public class PlaylistsController : ControllerBase
{
    private readonly IPlaylistService _playlists;

    public PlaylistsController(IPlaylistService playlists) => _playlists = playlists;

    /// <summary>Get the current user's playlists.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<PlaylistDto>>>> GetMine()
    {
        var list = await _playlists.GetMyPlaylistsAsync(User.GetAccountId());
        return Ok(ApiResponse<IEnumerable<PlaylistDto>>.Ok(list));
    }

    [HttpGet("{playlistId:guid}")]
    public async Task<ActionResult<ApiResponse<PlaylistDto>>> GetById(Guid playlistId)
    {
        var playlist = await _playlists.GetByIdAsync(playlistId, User.GetAccountId());
        return Ok(ApiResponse<PlaylistDto>.Ok(playlist));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<PlaylistDto>>> Create([FromBody] CreatePlaylistRequest request)
    {
        var playlist = await _playlists.CreateAsync(User.GetAccountId(), request);
        return CreatedAtAction(nameof(GetById), new { playlistId = playlist.PlaylistId },
            ApiResponse<PlaylistDto>.Ok(playlist, "Playlist created."));
    }

    [HttpPut("{playlistId:guid}")]
    public async Task<ActionResult<ApiResponse<PlaylistDto>>> Update(Guid playlistId, [FromBody] UpdatePlaylistRequest request)
    {
        var playlist = await _playlists.UpdateAsync(playlistId, User.GetAccountId(), request);
        return Ok(ApiResponse<PlaylistDto>.Ok(playlist, "Playlist updated."));
    }

    [HttpDelete("{playlistId:guid}")]
    public async Task<ActionResult<ApiResponse<MessageResponse>>> Delete(Guid playlistId)
    {
        var result = await _playlists.DeleteAsync(playlistId, User.GetAccountId());
        return Ok(ApiResponse<MessageResponse>.Ok(result, result.Message));
    }

    // ----- Playlist songs -----

    /// <summary>Get the songs in a playlist.</summary>
    [HttpGet("{playlistId:guid}/songs")]
    public async Task<ActionResult<ApiResponse<IEnumerable<PlaylistSongDto>>>> GetSongs(Guid playlistId)
    {
        var songs = await _playlists.GetSongsAsync(playlistId, User.GetAccountId());
        return Ok(ApiResponse<IEnumerable<PlaylistSongDto>>.Ok(songs));
    }

    [HttpPost("{playlistId:guid}/songs/{songId:guid}")]
    public async Task<ActionResult<ApiResponse<PlaylistSongDto>>> AddSong(Guid playlistId, Guid songId)
    {
        var membership = await _playlists.AddSongAsync(playlistId, User.GetAccountId(), songId);
        return Ok(ApiResponse<PlaylistSongDto>.Ok(membership, "Song added to playlist."));
    }

    [HttpDelete("{playlistId:guid}/songs/{songId:guid}")]
    public async Task<ActionResult<ApiResponse<MessageResponse>>> RemoveSong(Guid playlistId, Guid songId)
    {
        var result = await _playlists.RemoveSongAsync(playlistId, User.GetAccountId(), songId);
        return Ok(ApiResponse<MessageResponse>.Ok(result, result.Message));
    }
}
