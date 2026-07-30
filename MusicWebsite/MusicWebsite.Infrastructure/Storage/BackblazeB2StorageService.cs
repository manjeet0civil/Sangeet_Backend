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

    public async Task DeleteAsync(string? storageKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || IsAbsoluteUrl(storageKey))
            return;

        await _s3.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _settings.BucketName,
            Key = storageKey
        }, cancellationToken);
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
