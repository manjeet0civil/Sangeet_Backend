using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Songs;
using MusicWebsite.Application.Models;

namespace MusicWebsite.Application.Interfaces.Services;

public interface ISongService
{
    Task<IEnumerable<SongDto>> GetAllAsync(Guid? accountId = null);
    Task<SongDto> GetByIdAsync(Guid songId, Guid? accountId = null);
    Task<IEnumerable<SongDto>> SearchAsync(string searchText, Guid? accountId = null);
    /// <summary>Paginated list of the songs the given account uploaded (newest first).</summary>
    Task<PagedResult<SongDto>> GetMyUploadsAsync(Guid accountId, int page, int pageSize);
    Task<SongDto> CreateAsync(CreateSongRequest request, Guid uploadedByAccountId);
    Task<SongDto> UploadAsync(UploadSongRequest request, UploadFileInput audio, UploadFileInput? cover, Guid uploadedByAccountId, CancellationToken cancellationToken = default);
    Task<YoutubePreviewDto> GetYoutubePreviewAsync(string url, CancellationToken cancellationToken = default);
    Task<SongDto> ImportFromYoutubeAsync(ImportYoutubeRequest request, Guid uploadedByAccountId, CancellationToken cancellationToken = default);
    Task<SongDto> SetVoteAsync(Guid accountId, Guid songId, int value);
    /// <summary>Permanently deletes a song (and its cloud files). Enforces role/ownership.</summary>
    Task<MessageResponse> DeleteAsync(Guid songId, Guid callerAccountId, string callerRole, CancellationToken cancellationToken = default);
    Task<SongDto> UpdateAsync(Guid songId, UpdateSongRequest request);
}
