using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Songs;
using MusicWebsite.Application.Interfaces.Services;
using MusicWebsite.Application.Models;
using MusicWebsite.Extensions;
// YouTube import endpoints live below Upload; see README §"Import from YouTube".

namespace MusicWebsite.Controllers;

[ApiController]
[Authorize]
[Route("api/songs")]
public class SongsController : ControllerBase
{
    private readonly ISongService _songs;

    public SongsController(ISongService songs) => _songs = songs;

    /// <summary>List all songs, or filter with ?search=term (matches song name).</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<SongDto>>>> Get([FromQuery] string? search)
    {
        var accountId = User.GetAccountId();
        var songs = string.IsNullOrWhiteSpace(search)
            ? await _songs.GetAllAsync(accountId)
            : await _songs.SearchAsync(search, accountId);
        return Ok(ApiResponse<IEnumerable<SongDto>>.Ok(songs));
    }

    [HttpGet("{songId:guid}")]
    public async Task<ActionResult<ApiResponse<SongDto>>> GetById(Guid songId)
    {
        var song = await _songs.GetByIdAsync(songId, User.GetAccountId());
        return Ok(ApiResponse<SongDto>.Ok(song));
    }

    /// <summary>Paginated list of songs the current user uploaded (newest first). 10 per page by default.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<ApiResponse<PagedResult<SongDto>>>> MyUploads([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _songs.GetMyUploadsAsync(User.GetAccountId(), page, pageSize);
        return Ok(ApiResponse<PagedResult<SongDto>>.Ok(result));
    }

    /// <summary>
    /// Cast the current user's single up/down vote for a song. Body: { value: 1 | -1 | 0 }.
    /// The song's total priority (which drives search ranking) is recomputed and returned.
    /// </summary>
    [HttpPost("{songId:guid}/vote")]
    public async Task<ActionResult<ApiResponse<SongDto>>> Vote(Guid songId, [FromBody] SongVoteRequest request)
    {
        var song = await _songs.SetVoteAsync(User.GetAccountId(), songId, request.Value);
        return Ok(ApiResponse<SongDto>.Ok(song, "Vote recorded."));
    }

    /// <summary>
    /// Create a song from a direct URL (no file upload). Useful for externally-hosted audio.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<SongDto>>> Create([FromBody] CreateSongRequest request)
    {
        var song = await _songs.CreateAsync(request, User.GetAccountId());
        return CreatedAtAction(nameof(GetById), new { songId = song.SongId },
            ApiResponse<SongDto>.Ok(song, "Song created."));
    }

    /// <summary>
    /// Upload an MP3 (and optional cover image) to storage and save the song.
    /// Send as multipart/form-data: audioFile, coverImage (optional), songName, durationInSeconds?, priority?.
    /// </summary>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(104_857_600)]                                 // 100 MB
    [RequestFormLimits(MultipartBodyLengthLimit = 104_857_600)]
    public async Task<ActionResult<ApiResponse<SongDto>>> Upload(
        [FromForm] UploadSongRequest metadata,
        IFormFile audioFile,
        IFormFile? coverImage,
        CancellationToken cancellationToken)
    {
        if (audioFile is null || audioFile.Length == 0)
            throw new AppException("An audio file is required.", 400);

        await using var audioStream = audioFile.OpenReadStream();
        var audio = new UploadFileInput
        {
            Content = audioStream,
            FileName = audioFile.FileName,
            ContentType = audioFile.ContentType,
            Length = audioFile.Length
        };

        Stream? coverStream = null;
        UploadFileInput? cover = null;
        if (coverImage is not null && coverImage.Length > 0)
        {
            coverStream = coverImage.OpenReadStream();
            cover = new UploadFileInput
            {
                Content = coverStream,
                FileName = coverImage.FileName,
                ContentType = coverImage.ContentType,
                Length = coverImage.Length
            };
        }

        try
        {
            var song = await _songs.UploadAsync(metadata, audio, cover, User.GetAccountId(), cancellationToken);
            return CreatedAtAction(nameof(GetById), new { songId = song.SongId },
                ApiResponse<SongDto>.Ok(song, "Song uploaded."));
        }
        finally
        {
            if (coverStream is not null) await coverStream.DisposeAsync();
        }
    }

    /// <summary>
    /// Preview a YouTube link (title, duration, thumbnail) without downloading — used to confirm
    /// the right video before importing.
    /// </summary>
    [HttpPost("youtube/preview")]
    public async Task<ActionResult<ApiResponse<YoutubePreviewDto>>> YoutubePreview(
        [FromBody] YoutubeUrlRequest request, CancellationToken cancellationToken)
    {
        var preview = await _songs.GetYoutubePreviewAsync(request.Url, cancellationToken);
        return Ok(ApiResponse<YoutubePreviewDto>.Ok(preview));
    }

    /// <summary>
    /// Import a song from a YouTube link: extracts the audio-only track and the thumbnail, uploads
    /// both to cloud storage, and saves the song. Nothing is kept permanently on the server.
    /// </summary>
    [HttpPost("youtube")]
    public async Task<ActionResult<ApiResponse<SongDto>>> ImportYoutube(
        [FromBody] ImportYoutubeRequest request, CancellationToken cancellationToken)
    {
        var song = await _songs.ImportFromYoutubeAsync(request, User.GetAccountId(), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { songId = song.SongId },
            ApiResponse<SongDto>.Ok(song, "Song imported from YouTube."));
    }

    [HttpPut("{songId:guid}")]
    public async Task<ActionResult<ApiResponse<SongDto>>> Update(Guid songId, [FromBody] UpdateSongRequest request)
    {
        var song = await _songs.UpdateAsync(songId, request);
        return Ok(ApiResponse<SongDto>.Ok(song, "Song updated."));
    }

    /// <summary>
    /// Permanently delete a song and its cloud files. User → 403; Admin → only songs they uploaded;
    /// SuperAdmin → any song.
    /// </summary>
    [HttpDelete("{songId:guid}")]
    public async Task<ActionResult<ApiResponse<MessageResponse>>> Delete(Guid songId, CancellationToken cancellationToken)
    {
        var result = await _songs.DeleteAsync(songId, User.GetAccountId(), User.GetRole(), cancellationToken);
        return Ok(ApiResponse<MessageResponse>.Ok(result, result.Message));
    }
}
