namespace MusicWebsite.Application.Interfaces.Storage;

/// <summary>
/// Size caps for uploaded files, read from configuration.
///
/// Exists as an Application-layer port (like <c>IRoleDefaults</c>) because the concrete settings
/// live in Infrastructure, and Application must not depend on Infrastructure.
///
/// Why cap at all: anything much larger than a song is not a song. Without a limit, a single
/// request can push an arbitrarily large file into Backblaze B2 — storage you pay for, plus
/// egress every time it is streamed.
/// </summary>
public interface IUploadLimits
{
    /// <summary>Largest audio file a normal user (User/Admin) may upload or import, in MB.</summary>
    int MaxSongMegabytes { get; }

    /// <summary>Largest audio file a SuperAdmin may upload, in MB. Bounded by the API's own request limit.</summary>
    int MaxSongMegabytesSuperAdmin { get; }

    /// <summary>Largest cover image, in MB. Applies to everyone — a cover is never large.</summary>
    int MaxCoverMegabytes { get; }

    /// <summary>The audio cap that applies to the given role.</summary>
    int MaxSongMegabytesFor(string? role);
}
