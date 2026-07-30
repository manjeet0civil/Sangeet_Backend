-- =====================================================================
--  Sangeet — PostgreSQL schema (port of the SQL Server MusicDatabase)
--  Target: Supabase (PostgreSQL 15+), schema "public".
--  Idempotent: safe to run more than once.
--
--  Mapping decisions (see db/postgres/README.md):
--    uniqueidentifier -> uuid
--    nvarchar(n)      -> varchar(n)   (or citext where SQL Server compared case-insensitively)
--    nvarchar(MAX)    -> text
--    bit              -> boolean
--    datetime2        -> timestamp (no time zone; the app stores UTC, as GETUTCDATE() did)
--    GETUTCDATE()     -> timezone('utc', now())
--
--  Identifiers are lower-case (PostgreSQL folds unquoted names). Dapper matches result
--  columns to C# properties case-insensitively, so "songid" still fills SongId.
-- =====================================================================

-- citext = case-insensitive text. SQL Server's default collation is case-insensitive, so
-- Email / UserName / PlaylistName comparisons and unique constraints ignored case there.
-- Without this, 'A@b.com' and 'a@b.com' would become two different accounts.
CREATE EXTENSION IF NOT EXISTS citext;

-- ---------------------------------------------------------------- account
CREATE TABLE IF NOT EXISTS account (
    accountid     uuid         NOT NULL PRIMARY KEY,
    email         citext       NOT NULL UNIQUE,
    passwordhash  text         NOT NULL,
    isactive      boolean      NOT NULL DEFAULT true,
    created       timestamp    NOT NULL DEFAULT timezone('utc', now()),
    updated       timestamp     NULL,
    role          varchar(20)  NOT NULL DEFAULT 'User'
);

-- ------------------------------------------------------------------ users
CREATE TABLE IF NOT EXISTS users (
    userid           uuid         NOT NULL PRIMARY KEY,
    accountid        uuid         NOT NULL UNIQUE REFERENCES account(accountid) ON DELETE CASCADE,
    username         citext       NOT NULL,
    fullname         varchar(200) NOT NULL,
    profileimageurl  text          NULL,
    created          timestamp    NOT NULL DEFAULT timezone('utc', now()),
    updated          timestamp     NULL
);

-- ------------------------------------------------------------------ songs
CREATE TABLE IF NOT EXISTS songs (
    songid               uuid         NOT NULL PRIMARY KEY,
    songname             varchar(250) NOT NULL,
    songurl              text         NOT NULL,
    imageurl             text          NULL,
    durationinseconds    integer       NULL,
    priority             integer      NOT NULL DEFAULT 0,
    isdeleted            boolean      NOT NULL DEFAULT false,
    created              timestamp    NOT NULL DEFAULT timezone('utc', now()),
    updated              timestamp     NULL,
    contenthash          varchar(64)   NULL,
    sourcekey            varchar(100)  NULL,
    uploadedbyaccountid  uuid          NULL
);
-- No FK on uploadedbyaccountid — matches SQL Server, where a cascade-deleted account
-- deliberately leaves its uploads in the shared library with a dangling id.

-- Filtered unique indexes → PostgreSQL partial unique indexes (same semantics).
CREATE UNIQUE INDEX IF NOT EXISTS ux_songs_contenthash
    ON songs (contenthash) WHERE contenthash IS NOT NULL AND isdeleted = false;
CREATE UNIQUE INDEX IF NOT EXISTS ux_songs_sourcekey
    ON songs (sourcekey)   WHERE sourcekey   IS NOT NULL AND isdeleted = false;

-- -------------------------------------------------------------- playlists
CREATE TABLE IF NOT EXISTS playlists (
    playlistid    uuid         NOT NULL PRIMARY KEY,
    accountid     uuid         NOT NULL REFERENCES account(accountid) ON DELETE CASCADE,
    playlistname  citext       NOT NULL,
    created       timestamp    NOT NULL DEFAULT timezone('utc', now()),
    updated       timestamp     NULL
);

-- ---------------------------------------------------------- playlistsongs
CREATE TABLE IF NOT EXISTS playlistsongs (
    playlistsongid  uuid      NOT NULL PRIMARY KEY,
    playlistid      uuid      NOT NULL REFERENCES playlists(playlistid) ON DELETE CASCADE,
    songid          uuid      NOT NULL REFERENCES songs(songid)         ON DELETE CASCADE,
    created         timestamp NOT NULL DEFAULT timezone('utc', now())
);

-- ----------------------------------------------------------- songpriority
-- One row per (song, account) vote. Value is -1 or 1; a cleared vote deletes the row.
CREATE TABLE IF NOT EXISTS songpriority (
    songpriorityid  uuid      NOT NULL PRIMARY KEY,
    songid          uuid      NOT NULL REFERENCES songs(songid),
    accountid       uuid      NOT NULL REFERENCES account(accountid),
    value           integer   NOT NULL,
    created         timestamp NOT NULL DEFAULT timezone('utc', now()),
    updated         timestamp  NULL,
    CONSTRAINT ck_songpriority_value  CHECK (value = 1 OR value = -1),
    CONSTRAINT ux_songpriority_song_account UNIQUE (songid, accountid)
);

-- =====================================================================
--  Row Level Security
--  Supabase publishes every table in "public" through PostgREST, reachable with the
--  project's anon key. Enabling RLS with NO policies blocks that path completely
--  (password hashes must never be readable that way). The API connects as the table
--  owner over the direct Postgres connection, and owners bypass RLS — so the app is
--  unaffected. Do NOT add "FORCE ROW LEVEL SECURITY": that would lock the app out too.
-- =====================================================================
ALTER TABLE account       ENABLE ROW LEVEL SECURITY;
ALTER TABLE users         ENABLE ROW LEVEL SECURITY;
ALTER TABLE songs         ENABLE ROW LEVEL SECURITY;
ALTER TABLE playlists     ENABLE ROW LEVEL SECURITY;
ALTER TABLE playlistsongs ENABLE ROW LEVEL SECURITY;
ALTER TABLE songpriority  ENABLE ROW LEVEL SECURITY;
