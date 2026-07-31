namespace MusicWebsite.Infrastructure.Persistence;

/// <summary>Central list of stored procedure names so call sites stay typo-proof.</summary>
public static class StoredProcedures
{
    // Account
    public const string AccountInsert = "procAccountInsert";
    public const string AccountUpdate = "procAccountUpdate";
    public const string AccountDelete = "procAccountDelete";
    public const string AccountGetById = "procAccountGetById";
    public const string AccountLogin = "procAccountLogin";
    public const string AccountGetAllWithRole = "procAccountGetAllWithRole";
    public const string AccountSetRole = "procAccountSetRole";
    public const string AccountCascadeDelete = "procAccountCascadeDelete";
    public const string AccountGetByGoogle = "procAccountGetByGoogle";
    public const string AccountInsertGoogle = "procAccountInsertGoogle";
    public const string AccountLinkGoogle = "procAccountLinkGoogle";

    // Users
    public const string UserInsert = "procUserInsert";
    public const string UserUpdate = "procUserUpdate";
    public const string UserGetById = "procUserGetById";
    public const string UserGetByAccountId = "procUserGetByAccountId";
    public const string UserGetByUserName = "procUserGetByUserName";
    public const string UserGetAll = "procUserGetAll";

    // Songs
    public const string SongInsert = "procSongInsert";
    public const string SongUpdate = "procSongUpdate";
    public const string SongDelete = "procSongDelete";
    public const string SongGetById = "procSongGetById";
    public const string SongGetAll = "procSongGetAll";
    public const string SongSearch = "procSongSearch";
    public const string SongFindDuplicate = "procSongFindDuplicate";
    public const string SongVoteSet = "procSongVoteSet";
    public const string SongHardDelete = "procSongHardDelete";
    public const string SongGetByUploader = "procSongGetByUploader";
    public const string SongFindByTitleArtist = "procSongFindByTitleArtist";
    public const string SongSetLyrics = "procSongSetLyrics";

    // Categories
    public const string CategoryGetOrCreate = "procCategoryGetOrCreate";
    public const string CategoryGetAll = "procCategoryGetAll";

    // Playlists
    public const string PlaylistInsert = "procPlaylistInsert";
    public const string PlaylistUpdate = "procPlaylistUpdate";
    public const string PlaylistDelete = "procPlaylistDelete";
    public const string PlaylistGetById = "procPlaylistGetById";
    public const string PlaylistGetByAccountId = "procPlaylistGetByAccountId";

    // PlaylistSongs
    public const string PlaylistSongAdd = "procPlaylistSongAdd";
    public const string PlaylistSongRemove = "procPlaylistSongRemove";
    public const string PlaylistSongGetByPlaylistId = "procPlaylistSongGetByPlaylistId";
}
