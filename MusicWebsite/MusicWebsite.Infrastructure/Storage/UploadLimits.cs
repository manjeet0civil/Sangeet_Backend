using Microsoft.Extensions.Options;
using MusicWebsite.Application.Common;
using MusicWebsite.Application.Interfaces.Storage;

namespace MusicWebsite.Infrastructure.Storage;

/// <summary>
/// Reads the upload size caps from <c>Storage:B2</c> configuration.
///
/// Defaults are chosen for real music files rather than round numbers:
/// a 320 kbps MP3 is roughly 2.4 MB per minute, so 20 MB covers about 8 minutes — comfortably
/// more than any normal song, while still rejecting the large files that are not songs at all.
/// A 10 MB cap would only allow ~4 minutes at 320 kbps and would reject ordinary tracks.
/// </summary>
public class UploadLimits : IUploadLimits
{
    private readonly B2Settings _b2;

    public UploadLimits(IOptions<StorageSettings> settings) => _b2 = settings.Value.B2;

    public int MaxSongMegabytes => Positive(_b2.MaxUploadMegabytes, 20);

    public int MaxSongMegabytesSuperAdmin => Math.Max(
        Positive(_b2.MaxUploadMegabytesSuperAdmin, 100),
        MaxSongMegabytes);           // a SuperAdmin is never held to a tighter limit than a user

    public int MaxCoverMegabytes => Positive(_b2.MaxCoverMegabytes, 5);

    public int MaxSongMegabytesFor(string? role)
        => string.Equals(role, Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase)
            ? MaxSongMegabytesSuperAdmin
            : MaxSongMegabytes;

    /// <summary>Guards against a 0 or negative value in config silently blocking every upload.</summary>
    private static int Positive(int configured, int fallback) => configured > 0 ? configured : fallback;
}
