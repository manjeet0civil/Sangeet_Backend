namespace MusicWebsite.Domain.Entities;

public class Song
{
    public Guid SongId { get; set; }
    public string SongName { get; set; } = string.Empty;
    public string SongUrl { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int? DurationInSeconds { get; set; }
    public int Priority { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }

    /// <summary>SHA-256 of the uploaded audio file — used to reject exact-duplicate uploads.</summary>
    public string? ContentHash { get; set; }
    /// <summary>External source id (e.g. "youtube:&lt;videoId&gt;") — used to import a source only once.</summary>
    public string? SourceKey { get; set; }
    /// <summary>The account that uploaded/imported this song (its owner). Null for legacy songs.</summary>
    public Guid? UploadedByAccountId { get; set; }
    /// <summary>The current caller's own vote for this song (-1, 0, or 1). Not stored on the row.</summary>
    public int MyVote { get; set; }

    /// <summary>Performer, filled in automatically on upload from ID3 tags or YouTube metadata.</summary>
    public string? Artist { get; set; }
    /// <summary>Category this song belongs to. Categories are created on demand.</summary>
    public Guid? CategoryId { get; set; }
    /// <summary>Category name, joined in by the read functions. Not a column on the row.</summary>
    public string? Category { get; set; }
    /// <summary>Plain-text lyrics. Only returned by GetById — list endpoints stay lean.</summary>
    public string? Lyrics { get; set; }
    /// <summary>Timestamped LRC lyrics that can scroll in time with playback.</summary>
    public string? LyricsSynced { get; set; }
}
