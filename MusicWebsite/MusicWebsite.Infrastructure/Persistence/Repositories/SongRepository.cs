using MusicWebsite.Application.Common;
using MusicWebsite.Application.Interfaces.Persistence;
using MusicWebsite.Domain.Entities;

namespace MusicWebsite.Infrastructure.Persistence.Repositories;

public class SongRepository : RepositoryBase, ISongRepository
{
    public SongRepository(IDbConnectionFactory factory) : base(factory) { }

    public Task<Song> InsertAsync(Guid songId, string songName, string songUrl, string? imageUrl, int? durationInSeconds, int priority, string? contentHash = null, string? sourceKey = null, Guid? uploadedByAccountId = null, string? artist = null, Guid? categoryId = null)
        => QueryFirstAsync<Song>(StoredProcedures.SongInsert,
            new { SongId = songId, SongName = songName, SongUrl = songUrl, ImageUrl = imageUrl, DurationInSeconds = durationInSeconds, Priority = priority, ContentHash = contentHash, SourceKey = sourceKey, UploadedByAccountId = uploadedByAccountId, Artist = artist, CategoryId = categoryId });

    public Task<Song> UpdateAsync(Guid songId, string songName, string songUrl, string? imageUrl, int? durationInSeconds, int priority, string? artist = null, Guid? categoryId = null)
        => QueryFirstAsync<Song>(StoredProcedures.SongUpdate,
            new { SongId = songId, SongName = songName, SongUrl = songUrl, ImageUrl = imageUrl, DurationInSeconds = durationInSeconds, Priority = priority, Artist = artist, CategoryId = categoryId });

    public Task<Song?> GetByIdAsync(Guid songId, Guid? accountId = null)
        => QuerySingleOrDefaultAsync<Song>(StoredProcedures.SongGetById, new { SongId = songId, AccountId = accountId });

    public Task<IEnumerable<Song>> GetAllAsync(Guid? accountId = null)
        => QueryAsync<Song>(StoredProcedures.SongGetAll, new { AccountId = accountId });

    public Task<IEnumerable<Song>> SearchAsync(string searchText, Guid? accountId = null)
        => QueryAsync<Song>(StoredProcedures.SongSearch, new { SearchText = searchText, AccountId = accountId });

    public Task<(IEnumerable<Song> Items, int Total)> GetByUploaderPagedAsync(Guid accountId, int offset, int pageSize)
        => QueryPageAsync<Song>(StoredProcedures.SongGetByUploader,
            new { AccountId = accountId, Offset = offset, PageSize = pageSize });

    public Task<MessageResponse> DeleteAsync(Guid songId)
        => QueryFirstAsync<MessageResponse>(StoredProcedures.SongDelete, new { SongId = songId });

    public Task<MessageResponse> HardDeleteAsync(Guid songId)
        => QueryFirstAsync<MessageResponse>(StoredProcedures.SongHardDelete, new { SongId = songId });

    public Task<Song?> FindDuplicateAsync(string? contentHash, string? sourceKey)
        => QuerySingleOrDefaultAsync<Song>(StoredProcedures.SongFindDuplicate, new { ContentHash = contentHash, SourceKey = sourceKey });

    public Task<Song> SetVoteAsync(Guid accountId, Guid songId, int value)
        => QueryFirstAsync<Song>(StoredProcedures.SongVoteSet, new { AccountId = accountId, SongId = songId, Value = value });

    public Task<Song?> FindByTitleArtistAsync(string songName, string? artist)
        => QuerySingleOrDefaultAsync<Song>(StoredProcedures.SongFindByTitleArtist, new { SongName = songName, Artist = artist });

    public Task<MessageResponse> SetLyricsAsync(Guid songId, string? lyrics, string? lyricsSynced)
        => QueryFirstAsync<MessageResponse>(StoredProcedures.SongSetLyrics, new { SongId = songId, Lyrics = lyrics, LyricsSynced = lyricsSynced });
}
