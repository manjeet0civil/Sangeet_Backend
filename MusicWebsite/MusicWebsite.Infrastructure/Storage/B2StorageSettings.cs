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
    public int MaxUploadMegabytes { get; set; } = 100;
}
