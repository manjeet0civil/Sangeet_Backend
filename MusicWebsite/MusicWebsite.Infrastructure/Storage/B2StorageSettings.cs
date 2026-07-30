namespace MusicWebsite.Infrastructure.Storage;

public class StorageSettings
{
    public const string SectionName = "Storage";

    public string Provider { get; set; } = "Stub";
    public B2Settings B2 { get; set; } = new();
}

public class B2Settings
{
    public string ServiceUrl { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string ApplicationKey { get; set; } = string.Empty;
    public int PresignExpiryMinutes { get; set; } = 120;

    /// <summary>Largest audio file a User/Admin may upload or import (MB). ~8 min at 320 kbps.</summary>
    public int MaxUploadMegabytes { get; set; } = 20;

    /// <summary>Largest audio file a SuperAdmin may upload (MB). Bounded by the API request limit.</summary>
    public int MaxUploadMegabytesSuperAdmin { get; set; } = 100;

    /// <summary>Largest cover image (MB), for every role — a cover is never large.</summary>
    public int MaxCoverMegabytes { get; set; } = 5;
}
