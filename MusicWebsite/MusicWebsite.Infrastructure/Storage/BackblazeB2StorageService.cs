using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicWebsite.Application.Common;
using MusicWebsite.Application.Interfaces.Storage;
using MusicWebsite.Application.Models;

namespace MusicWebsite.Infrastructure.Storage;

/// <summary>
/// Stores files in a Backblaze B2 bucket via its S3-compatible API. Because the bucket is private,
/// stored values are object keys; <see cref="ResolveReadUrl"/> turns them into short-lived
/// presigned URLs the browser can stream from directly.
/// </summary>
public class BackblazeB2StorageService : IStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly B2Settings _settings;
    private readonly ILogger<BackblazeB2StorageService> _logger;

    public BackblazeB2StorageService(IAmazonS3 s3, IOptions<StorageSettings> settings, ILogger<BackblazeB2StorageService> logger)
    {
        _s3 = s3;
        _settings = settings.Value.B2;
        _logger = logger;
    }

    public async Task<StorageResult> UploadAsync(Stream content, string fileName, string contentType, string category, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(category, fileName);

        var request = new PutObjectRequest
        {
            BucketName = _settings.BucketName,
            Key = key,
            InputStream = content,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            AutoCloseStream = false,
            DisablePayloadSigning = true // B2 does not support streaming SigV4 payload signing
        };

        await _s3.PutObjectAsync(request, cancellationToken);
        _logger.LogInformation("Uploaded {Key} to B2 bucket {Bucket}", key, _settings.BucketName);

        return new StorageResult(ResolveReadUrl(key)!, key);
    }

    /// <summary>
    /// Permanently removes an object — every stored version of it, not just the newest.
    ///
    /// A plain S3 <c>DeleteObject</c> is NOT enough on Backblaze. B2 buckets keep all versions by
    /// default, and on a versioned bucket a keyless delete only writes a *delete marker*: the file
    /// disappears from listings and from the app, while the actual bytes stay and keep costing
    /// storage. An audit of this bucket found 15.4 MB held that way — files a SuperAdmin had
    /// deleted months earlier were still being paid for.
    ///
    /// So we enumerate every version of the key (including any older delete markers, which are
    /// themselves entries that accumulate) and delete each one by its version id. That frees the
    /// bytes immediately and behaves identically whether or not versioning is enabled — an
    /// unversioned bucket simply reports one version.
    /// </summary>
    public async Task DeleteAsync(string? storageKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || IsAbsoluteUrl(storageKey))
            return;

        List<(string Key, string VersionId)> versions;
        try
        {
            versions = await ListAllVersionsAsync(storageKey, cancellationToken);
        }
        catch (Exception ex)
        {
            // Listing versions needs the listAllBucketNames/listFiles capability on the B2
            // application key. If it's missing we can still hide the object, which is no worse
            // than the old behaviour — so degrade rather than block the user's delete.
            _logger.LogWarning(ex,
                "Could not list versions of {Key}; falling back to a single delete, which may leave bytes stored", storageKey);

            await _s3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _settings.BucketName,
                Key = storageKey
            }, cancellationToken);
            return;
        }

        if (versions.Count == 0)
        {
            _logger.LogInformation("Nothing to delete for {Key} — already gone from B2", storageKey);
            return;
        }

        foreach (var (key, versionId) in versions)
        {
            await _s3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _settings.BucketName,
                Key = key,
                VersionId = versionId
            }, cancellationToken);
        }

        _logger.LogInformation("Permanently deleted {Key} from B2 ({Count} version(s))", storageKey, versions.Count);
    }

    /// <summary>
    /// Every stored version of one exact key. <c>Prefix</c> only narrows the scan — it matches by
    /// prefix, so "songs/a.mp3" would also return "songs/a.mp3.bak" — hence the exact-key filter
    /// before anything is deleted.
    /// </summary>
    private async Task<List<(string Key, string VersionId)>> ListAllVersionsAsync(string storageKey, CancellationToken cancellationToken)
    {
        var found = new List<(string, string)>();
        string? keyMarker = null, versionMarker = null;

        do
        {
            var page = await _s3.ListVersionsAsync(new ListVersionsRequest
            {
                BucketName = _settings.BucketName,
                Prefix = storageKey,
                KeyMarker = keyMarker,
                VersionIdMarker = versionMarker
            }, cancellationToken);

            foreach (var version in page.Versions)
            {
                if (!string.Equals(version.Key, storageKey, StringComparison.Ordinal)) continue;
                if (string.IsNullOrEmpty(version.VersionId)) continue;
                found.Add((version.Key, version.VersionId));
            }

            keyMarker = page.IsTruncated == true ? page.NextKeyMarker : null;
            versionMarker = page.IsTruncated == true ? page.NextVersionIdMarker : null;
        } while (keyMarker is not null);

        return found;
    }

    public string? ResolveReadUrl(string? storageKeyOrUrl)
    {
        if (string.IsNullOrWhiteSpace(storageKeyOrUrl))
            return storageKeyOrUrl;

        // Legacy / externally-hosted values are already full URLs — leave them alone.
        if (IsAbsoluteUrl(storageKeyOrUrl))
            return storageKeyOrUrl;

        return _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _settings.BucketName,
            Key = storageKeyOrUrl,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(_settings.PresignExpiryMinutes)
        });
    }

    private static string BuildKey(string category, string fileName)
    {
        var ext = Path.GetExtension(fileName);
        var safeCategory = string.IsNullOrWhiteSpace(category) ? "files" : category.Trim('/');
        return $"{safeCategory}/{Guid.NewGuid():N}{ext}".ToLowerInvariant();
    }

    private static bool IsAbsoluteUrl(string value)
        => value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
