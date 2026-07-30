using MusicWebsite.Application.Common;
using MusicWebsite.Domain.Entities;

namespace MusicWebsite.Application.Interfaces.Persistence;

public interface ISongRepository
{
    Task<Song> InsertAsync(Guid songId, string songName, string songUrl, string? imageUrl, int? durationInSeconds, int priority, string? contentHash = null, string? sourceKey = null, Guid? uploadedByAccountId = null);
    Task<Song> UpdateAsync(Guid songId, string songName, string songUrl, string? imageUrl, int? durationInSeconds, int priority);
    Task<Song?> GetByIdAsync(Guid songId, Guid? accountId = null);
    Task<IEnumerable<Song>> GetAllAsync(Guid? accountId = null);
    Task<IEnumerable<Song>> SearchAsync(string searchText, Guid? accountId = null);
    /// <summary>One page of the songs a given account uploaded (newest first), plus the total count.</summary>
    Task<(IEnumerable<Song> Items, int Total)> GetByUploaderPagedAsync(Guid accountId, int offset, int pageSize);
    Task<MessageResponse> DeleteAsync(Guid songId);
    /// <summary>Permanently removes a song and all its playlist links + votes.</summary>
    Task<MessageResponse> HardDeleteAsync(Guid songId);

    /// <summary>Returns an existing live song matching the given content hash or source key, else null.</summary>
    Task<Song?> FindDuplicateAsync(string? contentHash, string? sourceKey);

    /// <summary>Sets the caller's single vote (-1/0/1) for a song and returns the song with its new total.</summary>
    Task<Song> SetVoteAsync(Guid accountId, Guid songId, int value);
}
