using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Playlists;
using MusicWebsite.Application.Interfaces.Persistence;
using MusicWebsite.Domain.Entities;

namespace MusicWebsite.Infrastructure.Persistence.Repositories;

public class PlaylistRepository : RepositoryBase, IPlaylistRepository
{
    public PlaylistRepository(IDbConnectionFactory factory) : base(factory) { }

    public Task<Playlist> InsertAsync(Guid playlistId, Guid accountId, string playlistName)
        => QueryFirstAsync<Playlist>(StoredProcedures.PlaylistInsert,
            new { PlaylistId = playlistId, AccountId = accountId, PlaylistName = playlistName });

    public Task<Playlist> UpdateAsync(Guid playlistId, string playlistName)
        => QueryFirstAsync<Playlist>(StoredProcedures.PlaylistUpdate,
            new { PlaylistId = playlistId, PlaylistName = playlistName });

    public Task<PlaylistDto?> GetByIdAsync(Guid playlistId)
        => QuerySingleOrDefaultAsync<PlaylistDto>(StoredProcedures.PlaylistGetById,
            new { PlaylistId = playlistId });

    public Task<IEnumerable<PlaylistDto>> GetByAccountIdAsync(Guid accountId)
        => QueryAsync<PlaylistDto>(StoredProcedures.PlaylistGetByAccountId,
            new { AccountId = accountId });

    public Task<MessageResponse> DeleteAsync(Guid playlistId)
        => QueryFirstAsync<MessageResponse>(StoredProcedures.PlaylistDelete,
            new { PlaylistId = playlistId });
}
