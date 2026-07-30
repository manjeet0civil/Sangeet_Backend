using MusicWebsite.Application.Models;

namespace MusicWebsite.Application.Interfaces.Storage;

/// <summary>
/// Abstraction over file storage (MP3s, cover images). Swap the implementation (Backblaze B2,
/// local disk, Azure Blob, …) without touching callers.
/// </summary>
public interface IStorageService
{
    /// <summary>Uploads a file and returns its stable storage key + a servable URL.</summary>
    Task<StorageResult> UploadAsync(Stream content, string fileName, string contentType, string category, CancellationToken cancellationToken = default);

    /// <summary>Removes a file by its storage key. No-op if the key is null/empty.</summary>
    Task DeleteAsync(string? storageKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a stored value to a URL a client can read/stream. If the value is already an
    /// absolute http(s) URL it is returned unchanged; otherwise it is treated as a storage key
    /// and turned into a (possibly time-limited) URL.
    /// </summary>
    string? ResolveReadUrl(string? storageKeyOrUrl);
}
