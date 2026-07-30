using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Songs;
using MusicWebsite.Application.Interfaces.Media;
using MusicWebsite.Application.Interfaces.Persistence;
using MusicWebsite.Application.Interfaces.Services;
using MusicWebsite.Application.Interfaces.Storage;
using MusicWebsite.Application.Models;
using MusicWebsite.Domain.Entities;

namespace MusicWebsite.Application.Services;

public class SongService : ISongService
{
    private static readonly HashSet<string> AllowedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".m4a", ".aac", ".wav", ".ogg", ".flac" };
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    /// <summary>Where a song lands when nothing tells us what it is.</summary>
    private const string DefaultCategory = "Uncategorized";

    private readonly ISongRepository _songs;
    private readonly ICategoryRepository _categories;
    private readonly IStorageService _storage;
    private readonly IYoutubeAudioExtractor _youtube;
    private readonly IUploadLimits _limits;
    private readonly ITrackTagReader _tags;
    private readonly ILyricsProvider _lyrics;
    private readonly ILogger<SongService> _logger;

    public SongService(ISongRepository songs, ICategoryRepository categories, IStorageService storage,
        IYoutubeAudioExtractor youtube, IUploadLimits limits, ITrackTagReader tags, ILyricsProvider lyrics,
        ILogger<SongService> logger)
    {
        _logger = logger;
        _songs = songs;
        _categories = categories;
        _storage = storage;
        _youtube = youtube;
        _limits = limits;
        _tags = tags;
        _lyrics = lyrics;
    }

    /// <summary>
    /// Rejects files that are too large before a single byte reaches storage.
    /// Anything much bigger than a song is not a song, and every stored byte costs storage plus
    /// egress on each play. SuperAdmin gets a higher cap; everyone shares the cover-image cap.
    /// </summary>
    private static void EnforceSize(long bytes, int maxMegabytes, string what)
    {
        var maxBytes = (long)maxMegabytes * 1024 * 1024;
        if (bytes <= maxBytes) return;

        var actualMb = bytes / (1024.0 * 1024.0);
        throw new AppException(
            $"This {what} is {actualMb:0.#} MB, over the {maxMegabytes} MB limit. " +
            $"Use a smaller file or a lower bitrate.", 413);
    }

    public async Task<IEnumerable<SongDto>> GetAllAsync(Guid? accountId = null)
        => (await _songs.GetAllAsync(accountId)).Select(Map);

    public async Task<SongDto> GetByIdAsync(Guid songId, Guid? accountId = null)
    {
        var song = await _songs.GetByIdAsync(songId, accountId)
            ?? throw new AppException("Song not found.", 404);
        return Map(song);
    }

    public async Task<IEnumerable<SongDto>> SearchAsync(string searchText, Guid? accountId = null)
        => (await _songs.SearchAsync(searchText ?? string.Empty, accountId)).Select(Map);

    public async Task<PagedResult<SongDto>> GetMyUploadsAsync(Guid accountId, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var (items, total) = await _songs.GetByUploaderPagedAsync(accountId, (page - 1) * pageSize, pageSize);
        return new PagedResult<SongDto>
        {
            Items = items.Select(Map).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<SongDto> SetVoteAsync(Guid accountId, Guid songId, int value)
    {
        var song = await _songs.SetVoteAsync(accountId, songId, value);
        return Map(song);
    }

    public async Task<SongDto> CreateAsync(CreateSongRequest request, Guid uploadedByAccountId)
    {
        var song = await _songs.InsertAsync(
            Guid.NewGuid(),
            request.SongName,
            request.SongUrl,
            request.ImageUrl,
            request.DurationInSeconds,
            request.Priority,
            uploadedByAccountId: uploadedByAccountId);
        return Map(song);
    }

    public async Task<SongDto> UploadAsync(UploadSongRequest request, UploadFileInput audio, UploadFileInput? cover, Guid uploadedByAccountId, string callerRole, CancellationToken cancellationToken = default)
    {
        ValidateExtension(audio.FileName, AllowedAudioExtensions, "audio");
        if (cover is not null)
            ValidateExtension(cover.FileName, AllowedImageExtensions, "image");

        // Size check first — cheapest rejection, and it happens before hashing or any storage call.
        EnforceSize(audio.Length, _limits.MaxSongMegabytesFor(callerRole), "song");
        if (cover is not null)
            EnforceSize(cover.Length, _limits.MaxCoverMegabytes, "cover image");

        // Fingerprint the audio and reject exact duplicates BEFORE spending an upload to storage.
        var (contentHash, audioContent) = await HashAndRewindAsync(audio.Content, cancellationToken);
        if (await _songs.FindDuplicateAsync(contentHash, null) is not null)
            throw new AppException("This exact audio file is already in the library.", 409);

        // Read the file's own tags for artist/genre/duration. What the uploader typed always wins;
        // the tags only fill the gaps. HashAndRewindAsync guarantees a seekable stream by here,
        // which TagLib needs, and the reader restores the position for the upload below.
        var metadata = new TrackMetadata(
                Title: request.SongName,
                Artist: request.Artist,
                Category: request.Category,
                DurationInSeconds: request.DurationInSeconds)
            .FillFrom(_tags.Read(audioContent, audio.FileName));

        // Same song, same artist, already here — refuse, so the library doesn't fill with copies
        // of one track under slightly different file names.
        await EnsureNotAlreadyPresentAsync(metadata, callerRole);

        var categoryId = await ResolveCategoryIdAsync(metadata.Category);

        // Upload audio (and optional cover) to storage; store the returned keys in the DB.
        var audioResult = await _storage.UploadAsync(audioContent, audio.FileName, audio.ContentType, "songs", cancellationToken);

        string? coverKey = null;
        try
        {
            if (cover is not null)
            {
                var coverResult = await _storage.UploadAsync(cover.Content, cover.FileName, cover.ContentType, "covers", cancellationToken);
                coverKey = coverResult.StorageKey;
            }

            var song = await _songs.InsertAsync(
                Guid.NewGuid(),
                TrimName(metadata.Title ?? request.SongName),
                audioResult.StorageKey,
                coverKey,
                metadata.DurationInSeconds,
                request.Priority,
                contentHash: contentHash,
                uploadedByAccountId: uploadedByAccountId,
                artist: metadata.Artist,
                categoryId: categoryId);

            return await WithLyricsAsync(song, metadata, cancellationToken);
        }
        catch
        {
            // Roll back uploaded blobs if metadata persistence fails, to avoid orphaned files.
            await _storage.DeleteAsync(audioResult.StorageKey, cancellationToken);
            if (coverKey is not null) await _storage.DeleteAsync(coverKey, cancellationToken);
            throw;
        }
    }

    public async Task<YoutubePreviewDto> GetYoutubePreviewAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new AppException("A YouTube URL is required.", 400);

        var preview = await _youtube.GetPreviewAsync(url.Trim(), cancellationToken);
        var suggested = ResolveYoutubeMetadata(preview.Title, preview.Author, preview.Music,
            preview.DurationInSeconds, TrackMetadata.Empty);

        return new YoutubePreviewDto
        {
            Title = preview.Title,
            Author = preview.Author,
            DurationInSeconds = preview.DurationInSeconds,
            ThumbnailUrl = preview.ThumbnailUrl,
            SuggestedSongName = suggested.Title,
            SuggestedArtist = suggested.Artist,
            SuggestedCategory = suggested.Category ?? DefaultCategory
        };
    }

    public async Task<SongDto> ImportFromYoutubeAsync(ImportYoutubeRequest request, Guid uploadedByAccountId, string callerRole, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            throw new AppException("A YouTube URL is required.", 400);

        // Parse the canonical video id locally (no network). If we've already imported this exact
        // video, return the existing song and skip the whole download+upload — one import per video.
        var videoId = _youtube.TryGetVideoId(request.Url.Trim());
        var sourceKey = videoId is null ? null : $"youtube:{videoId}";
        if (sourceKey is not null)
        {
            var existing = await _songs.FindDuplicateAsync(null, sourceKey);
            if (existing is not null)
                return Map(existing);
        }

        // Extract the audio-only track to a temp file (disposed at the end of this method, which
        // deletes it — nothing accumulates on the server).
        await using var media = await _youtube.ExtractAsync(request.Url.Trim(), cancellationToken);

        // Same cap as a direct upload. Checked after extraction (the size isn't known until the
        // audio exists) but before anything is sent to storage — the temp file is deleted when
        // `media` is disposed, so a rejected import leaves nothing behind, locally or in B2.
        EnforceSize(media.AudioSizeBytes, _limits.MaxSongMegabytesFor(callerRole), "song");

        var metadata = ResolveYoutubeMetadata(media.Title, media.Author, media.Music, media.DurationInSeconds,
            new TrackMetadata(Title: request.SongName, Artist: request.Artist, Category: request.Category));
        var songName = TrimName(metadata.Title ?? media.Title);

        // A different YouTube link to the same recording is still the same song. The source-key
        // check above only catches the identical video, so match on title + artist as well.
        await EnsureNotAlreadyPresentAsync(metadata with { Title = songName }, callerRole);

        var categoryId = await ResolveCategoryIdAsync(metadata.Category);

        // Upload the audio to permanent storage (Backblaze B2), then the thumbnail as the cover.
        await using var audioStream = media.OpenAudio();
        var audioResult = await _storage.UploadAsync(audioStream, media.AudioFileName, media.AudioContentType, "songs", cancellationToken);

        string? coverKey = null;
        try
        {
            var thumbStream = media.OpenThumbnail();
            if (thumbStream is not null)
            {
                await using (thumbStream)
                {
                    var coverResult = await _storage.UploadAsync(thumbStream, media.ThumbnailFileName!, media.ThumbnailContentType!, "covers", cancellationToken);
                    coverKey = coverResult.StorageKey;
                }
            }

            var song = await _songs.InsertAsync(
                Guid.NewGuid(),
                songName,
                audioResult.StorageKey,
                coverKey,
                metadata.DurationInSeconds ?? media.DurationInSeconds,
                request.Priority,
                sourceKey: sourceKey,
                uploadedByAccountId: uploadedByAccountId,
                artist: metadata.Artist,
                categoryId: categoryId);

            return await WithLyricsAsync(song, metadata with { Title = songName }, cancellationToken);
        }
        catch
        {
            // Roll back uploaded blobs if metadata persistence fails, to avoid orphaned files.
            await _storage.DeleteAsync(audioResult.StorageKey, cancellationToken);
            if (coverKey is not null) await _storage.DeleteAsync(coverKey, cancellationToken);
            throw;
        }
    }

    public async Task<SongDto> UpdateAsync(Guid songId, UpdateSongRequest request)
    {
        // A blank category means "leave it alone" rather than "move to Uncategorized" — the
        // update function keeps the existing value when it receives null.
        Guid? categoryId = string.IsNullOrWhiteSpace(request.Category)
            ? null
            : (await _categories.GetOrCreateAsync(request.Category)).CategoryId;

        var song = await _songs.UpdateAsync(
            songId,
            request.SongName,
            request.SongUrl,
            request.ImageUrl,
            request.DurationInSeconds,
            request.Priority,
            artist: request.Artist,
            categoryId: categoryId);
        return Map(song);
    }

    public async Task<IEnumerable<CategoryDto>> GetCategoriesAsync()
        => (await _categories.GetAllAsync())
            .Select(c => new CategoryDto { CategoryId = c.CategoryId, Name = c.Name, TotalSongs = c.TotalSongs });

    public async Task<MessageResponse> DeleteAsync(Guid songId, Guid callerAccountId, string callerRole, CancellationToken cancellationToken = default)
    {
        var song = await _songs.GetByIdAsync(songId)
            ?? throw new AppException("Song not found.", 404);

        // Authorize: SuperAdmin may delete anything; Admin only their own uploads; User never.
        var isSuperAdmin = string.Equals(callerRole, Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase);
        var isAdmin = string.Equals(callerRole, Roles.Admin, StringComparison.OrdinalIgnoreCase);
        var ownsIt = song.UploadedByAccountId == callerAccountId;

        if (!isSuperAdmin && !(isAdmin && ownsIt))
            throw new AppException(
                isAdmin ? "Admins can only delete songs they uploaded." : "You don't have permission to delete songs.",
                403);

        // Permanently remove the cloud files first (frees B2 space), then the DB row + links + votes.
        // DeleteAsync no-ops on null/absolute-URL values, so externally-hosted songs are safe.
        await _storage.DeleteAsync(song.SongUrl, cancellationToken);
        await _storage.DeleteAsync(song.ImageUrl, cancellationToken);

        return await _songs.HardDeleteAsync(songId);
    }

    /// <summary>
    /// Layers the metadata sources for a YouTube import, best first:
    ///
    ///   1. what the uploader typed — an explicit correction always wins;
    ///   2. YouTube's own structured music fields, which come from the label rather than the
    ///      uploader and are the most trustworthy thing available;
    ///   3. the title parser, which strips "| Movie | Cast | Label" noise off the video title;
    ///   4. the raw title, so there is always at least a name.
    /// </summary>
    private static TrackMetadata ResolveYoutubeMetadata(string rawTitle, string? uploader,
        TrackMetadata? youtubeMusic, int? duration, TrackMetadata explicitValues)
        => explicitValues
            .FillFrom(youtubeMusic ?? TrackMetadata.Empty)
            .FillFrom(TrackTitleParser.Parse(rawTitle, uploader))
            .FillFrom(new TrackMetadata(Title: rawTitle, DurationInSeconds: duration));

    /// <summary>
    /// Blocks re-uploading a song that is already in the library under the same title and artist.
    ///
    /// SuperAdmin is exempt on purpose: a cover, a remix, a live cut and a re-recording are all
    /// legitimately "the same title by the same artist", and someone has to be able to add them.
    /// </summary>
    private async Task EnsureNotAlreadyPresentAsync(TrackMetadata metadata, string callerRole)
    {
        if (string.Equals(callerRole, Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase)) return;
        if (string.IsNullOrWhiteSpace(metadata.Title)) return;

        var existing = await _songs.FindByTitleArtistAsync(metadata.Title, metadata.Artist);
        if (existing is null) return;

        var by = string.IsNullOrWhiteSpace(metadata.Artist) ? "" : $" by {metadata.Artist}";
        throw new AppException($"\"{metadata.Title}\"{by} is already in the library.", 409);
    }

    /// <summary>
    /// Resolves a category name to its id, creating the category if this is the first song to use
    /// it. That is what makes categories self-maintaining: nobody has to seed a list up front.
    /// </summary>
    private async Task<Guid?> ResolveCategoryIdAsync(string? name)
    {
        var category = await _categories.GetOrCreateAsync(
            string.IsNullOrWhiteSpace(name) ? DefaultCategory : name.Trim());
        return category.CategoryId;
    }

    /// <summary>
    /// Looks lyrics up and attaches them to the saved song. Strictly best-effort and always last:
    /// the song row already exists by this point, so a lyrics miss or an LRCLIB outage costs the
    /// uploader nothing but a blank lyrics panel.
    /// </summary>
    private async Task<SongDto> WithLyricsAsync(Song song, TrackMetadata metadata, CancellationToken cancellationToken)
    {
        var dto = Map(song);

        var title = metadata.Title ?? song.SongName;
        if (string.IsNullOrWhiteSpace(title)) return dto;

        var found = await _lyrics.FindAsync(title, metadata.Artist ?? song.Artist, metadata.Album,
            song.DurationInSeconds ?? metadata.DurationInSeconds, cancellationToken);
        if (found is null) return dto;

        try
        {
            await _songs.SetLyricsAsync(song.SongId, found.Plain, found.Synced);
        }
        catch (Exception ex)
        {
            // The song, its audio and its cover are already saved and safe. Storing lyrics is the
            // last and least important step, so a failure here must not turn a successful upload
            // into an error the uploader sees — they'd be told it failed while the song sits
            // perfectly fine in the library.
            _logger.LogWarning(ex, "Could not store lyrics for song {SongId}", song.SongId);
            return dto;
        }

        dto.Lyrics = found.Plain;
        dto.LyricsSynced = found.Synced;
        return dto;
    }

    // Song names are capped at 250 chars in the DB; YouTube titles can be longer.
    private static string TrimName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed)) return "Untitled";
        return trimmed.Length <= 250 ? trimmed : trimmed[..250];
    }

    private static void ValidateExtension(string fileName, HashSet<string> allowed, string kind)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || !allowed.Contains(ext))
            throw new AppException($"Unsupported {kind} file type '{ext}'. Allowed: {string.Join(", ", allowed)}.", 400);
    }

    /// <summary>
    /// Computes the SHA-256 hex hash of a stream and returns it with a stream rewound to the start
    /// so the same bytes can still be uploaded. Buffers to memory first if the input can't seek.
    /// </summary>
    private static async Task<(string hash, Stream content)> HashAndRewindAsync(Stream input, CancellationToken cancellationToken)
    {
        Stream content = input;
        if (!input.CanSeek)
        {
            var buffer = new MemoryStream();
            await input.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            content = buffer;
        }

        using var sha = SHA256.Create();
        var digest = await sha.ComputeHashAsync(content, cancellationToken);
        content.Position = 0;
        return (Convert.ToHexString(digest).ToLowerInvariant(), content);
    }

    private SongDto Map(Song s) => new()
    {
        SongId = s.SongId,
        SongName = s.SongName,
        SongUrl = _storage.ResolveReadUrl(s.SongUrl) ?? s.SongUrl,
        ImageUrl = _storage.ResolveReadUrl(s.ImageUrl),
        DurationInSeconds = s.DurationInSeconds,
        Priority = s.Priority,
        MyVote = s.MyVote,
        UploadedByAccountId = s.UploadedByAccountId,
        Created = s.Created == default ? null : s.Created,
        Updated = s.Updated,
        Artist = s.Artist,
        CategoryId = s.CategoryId,
        Category = s.Category,
        Lyrics = s.Lyrics,
        LyricsSynced = s.LyricsSynced
    };
}
