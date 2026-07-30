using MusicWebsite.Application.Common;
using MusicWebsite.Application.Interfaces.Storage;
using MusicWebsite.Application.Models;

namespace MusicWebsite.Infrastructure.Storage;

/// <summary>
/// Fallback used when no real storage provider is configured. Upload is unavailable; stored values
/// are assumed to be URLs the client supplied directly, so <see cref="ResolveReadUrl"/> is a pass-through.
/// </summary>
public class StubStorageService : IStorageService
{
    public Task<StorageResult> UploadAsync(Stream content, string fileName, string contentType, string category, CancellationToken cancellationToken = default)
        => throw new AppException("File upload is not configured. Provide a direct URL instead.", 501);

    public Task DeleteAsync(string? storageKey, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public string? ResolveReadUrl(string? storageKeyOrUrl) => storageKeyOrUrl;
}
