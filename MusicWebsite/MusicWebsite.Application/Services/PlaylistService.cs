using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Playlists;
using MusicWebsite.Application.Interfaces.Persistence;
using MusicWebsite.Application.Interfaces.Services;
using MusicWebsite.Application.Interfaces.Storage;

namespace MusicWebsite.Application.Services;

public class PlaylistService : IPlaylistService
{
    private readonly IPlaylistRepository _playlists;
    private readonly IPlaylistSongRepository _playlistSongs;
    private readonly ISongRepository _songs;
    private readonly IStorageService _storage;

    public PlaylistService(
        IPlaylistRepository playlists,
        IPlaylistSongRepository playlistSongs,
        ISongRepository songs,
        IStorageService storage)
    {
        _playlists = playlists;
        _playlistSongs = playlistSongs;
        _songs = songs;
        _storage = storage;
    }

    public async Task<IEnumerable<PlaylistDto>> GetMyPlaylistsAsync(Guid accountId)
    {
        var list = await _playlists.GetByAccountIdAsync(accountId);
        // procPlaylistGetByAccountId does not project AccountId; set it for a consistent response.
        foreach (var p in list) p.AccountId = accountId;
        return list;
    }

    public Task<PlaylistDto> GetByIdAsync(Guid playlistId, Guid accountId)
        => GetOwnedAsync(playlistId, accountId);

    public async Task<PlaylistDto> CreateAsync(Guid accountId, CreatePlaylistRequest request)
    {
        var playlist = await _playlists.InsertAsync(Guid.NewGuid(), accountId, request.PlaylistName);
        return new PlaylistDto
        {
            PlaylistId = playlist.PlaylistId,
            AccountId = playlist.AccountId,
            PlaylistName = playlist.PlaylistName,
            TotalSongs = 0,
            Created = playlist.Created,
            Updated = playlist.Updated
        };
    }

    public async Task<PlaylistDto> UpdateAsync(Guid playlistId, Guid accountId, UpdatePlaylistRequest request)
    {
        await GetOwnedAsync(playlistId, accountId); // ownership guard
        var playlist = await _playlists.UpdateAsync(playlistId, request.PlaylistName);
        return new PlaylistDto
        {
            PlaylistId = playlist.PlaylistId,
            AccountId = playlist.AccountId,
            PlaylistName = playlist.PlaylistName,
            Created = playlist.Created,
            Updated = playlist.Updated
        };
    }

    public async Task<MessageResponse> DeleteAsync(Guid playlistId, Guid accountId)
    {
        await GetOwnedAsync(playlistId, accountId);
        return await _playlists.DeleteAsync(playlistId);
    }

    public async Task<IEnumerable<PlaylistSongDto>> GetSongsAsync(Guid playlistId, Guid accountId)
    {
        await GetOwnedAsync(playlistId, accountId);
        var songs = await _playlistSongs.GetByPlaylistIdAsync(playlistId);
        foreach (var s in songs)
        {
            s.SongUrl = _storage.ResolveReadUrl(s.SongUrl) ?? s.SongUrl;
            s.ImageUrl = _storage.ResolveReadUrl(s.ImageUrl);
        }
        return songs;
    }

    public async Task<PlaylistSongDto> AddSongAsync(Guid playlistId, Guid accountId, Guid songId)
    {
        await GetOwnedAsync(playlistId, accountId);
        var membership = await _playlistSongs.AddAsync(Guid.NewGuid(), playlistId, songId);

        // Enrich with song details for the response (the add proc validated the song exists).
        var song = await _songs.GetByIdAsync(songId);
        return new PlaylistSongDto
        {
            PlaylistSongId = membership.PlaylistSongId,
            SongId = songId,
            SongName = song?.SongName ?? string.Empty,
            SongUrl = _storage.ResolveReadUrl(song?.SongUrl) ?? song?.SongUrl ?? string.Empty,
            ImageUrl = _storage.ResolveReadUrl(song?.ImageUrl),
            DurationInSeconds = song?.DurationInSeconds,
            Priority = song?.Priority ?? 0,
            AddedOn = membership.Created
        };
    }

    public async Task<MessageResponse> RemoveSongAsync(Guid playlistId, Guid accountId, Guid songId)
    {
        await GetOwnedAsync(playlistId, accountId);
        return await _playlistSongs.RemoveAsync(playlistId, songId);
    }

    /// <summary>Fetches a playlist and verifies it belongs to the requesting account.</summary>
    private async Task<PlaylistDto> GetOwnedAsync(Guid playlistId, Guid accountId)
    {
        var playlist = await _playlists.GetByIdAsync(playlistId)
            ?? throw new AppException("Playlist does not exist.", 404);

        if (playlist.AccountId != accountId)
            throw new AppException("You do not have access to this playlist.", 403);

        return playlist;
    }
}
