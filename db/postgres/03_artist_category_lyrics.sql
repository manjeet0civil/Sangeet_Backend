-- =====================================================================
--  Sangeet — add Artist, Category and Lyrics
--
--  Run AFTER 01_schema.sql and 02_functions.sql. Idempotent.
--
--  APPLY THIS BEFORE DEPLOYING THE MATCHING CODE. Every change here is additive:
--  new columns are nullable and new result columns are appended, and Dapper ignores
--  columns it doesn't recognise — so the currently-deployed API keeps working against
--  this schema until the new build rolls out.
--
--  Wrapped in a transaction because several functions must be DROPped and recreated:
--  PostgreSQL refuses CREATE OR REPLACE when a function's return columns change
--  ("cannot change return type of existing function"). DDL is transactional here, so
--  the API never observes a missing function.
-- =====================================================================

BEGIN;

-- ---------------------------------------------------------------- category
-- Categories are created on demand: the first song that declares "Bollywood"
-- creates it, and everything after reuses it. citext + UNIQUE means "bollywood"
-- and "Bollywood" are the same category rather than two.
CREATE TABLE IF NOT EXISTS category (
    categoryid uuid      NOT NULL PRIMARY KEY,
    name       citext    NOT NULL UNIQUE,
    created    timestamp NOT NULL DEFAULT timezone('utc', now())
);

ALTER TABLE category ENABLE ROW LEVEL SECURITY;

-- ------------------------------------------------------------- song columns
-- artist is citext so duplicate detection is case-insensitive, matching how
-- songname/email/username already behave.
ALTER TABLE songs ADD COLUMN IF NOT EXISTS artist       citext NULL;
ALTER TABLE songs ADD COLUMN IF NOT EXISTS categoryid   uuid   NULL REFERENCES category(categoryid);
ALTER TABLE songs ADD COLUMN IF NOT EXISTS lyrics       text   NULL;   -- plain text
ALTER TABLE songs ADD COLUMN IF NOT EXISTS lyricssynced text   NULL;   -- LRC, timestamped

-- Supports the (name, artist) duplicate lookup. NOT unique on purpose: a SuperAdmin
-- is allowed to add a genuine cover, remix or live version of an existing title.
CREATE INDEX IF NOT EXISTS ix_songs_name_artist
    ON songs (songname, artist) WHERE isdeleted = false;

CREATE INDEX IF NOT EXISTS ix_songs_categoryid
    ON songs (categoryid) WHERE isdeleted = false;

-- =====================================================================
--  Category functions
-- =====================================================================

-- Returns the id for a category name, creating it if it does not exist yet.
--
-- NOTE the bare "ON CONFLICT DO NOTHING" with no column list. This function has an OUT
-- column called "name", so writing "ON CONFLICT (name)" makes PL/pgSQL complain that the
-- reference is ambiguous — it cannot tell the variable from the table column. Omitting the
-- target sidesteps that while still absorbing the unique violation, which is what keeps two
-- uploads racing for the same new category name from failing.
CREATE OR REPLACE FUNCTION proccategorygetorcreate(p_name varchar)
RETURNS TABLE (categoryid uuid, name citext, created timestamp)
LANGUAGE plpgsql AS $$
DECLARE
    v_name citext;
BEGIN
    v_name := btrim(COALESCE(p_name, ''))::citext;
    IF v_name = '' THEN
        v_name := 'Uncategorized';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM category c WHERE c.name = v_name) THEN
        INSERT INTO category (categoryid, name, created)
        VALUES (gen_random_uuid(), v_name, timezone('utc', now()))
        ON CONFLICT DO NOTHING;
    END IF;

    RETURN QUERY
    SELECT c.categoryid, c.name, c.created FROM category c WHERE c.name = v_name;
END;
$$;

-- Every category with how many live songs use it — for browsing and for the
-- category picker on the upload screen.
CREATE OR REPLACE FUNCTION proccategorygetall()
RETURNS TABLE (categoryid uuid, name citext, created timestamp, totalsongs integer)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT c.categoryid, c.name, c.created,
           CAST(COUNT(s.songid) FILTER (WHERE s.isdeleted = false) AS integer)
    FROM category c
    LEFT JOIN songs s ON s.categoryid = c.categoryid
    GROUP BY c.categoryid, c.name, c.created
    ORDER BY c.name ASC;
END;
$$;

-- =====================================================================
--  Duplicate detection by title + artist
-- =====================================================================

-- Returns a live song with the same name and artist, if one exists.
-- citext makes the comparison case-insensitive; both sides are trimmed so
-- " Tum Hi Ho " and "tum hi ho" match.
-- Artist NULL matches artist NULL, so untagged uploads still dedupe by title.
CREATE OR REPLACE FUNCTION procsongfindbytitleartist(
    p_songname varchar,
    p_artist   varchar DEFAULT NULL
)
RETURNS TABLE (songid uuid, songname varchar, artist citext, uploadedbyaccountid uuid, created timestamp)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT s.songid, s.songname, s.artist, s.uploadedbyaccountid, s.created
    FROM songs s
    WHERE s.isdeleted = false
      AND btrim(s.songname)::citext = btrim(COALESCE(p_songname, ''))::citext
      AND (
            (s.artist IS NULL AND (p_artist IS NULL OR btrim(p_artist) = ''))
         OR btrim(s.artist)::citext = btrim(COALESCE(p_artist, ''))::citext
          )
    LIMIT 1;
END;
$$;

-- =====================================================================
--  Song functions — dropped and recreated because their result shape changes
-- =====================================================================

DROP FUNCTION IF EXISTS procsonginsert(uuid, varchar, text, text, integer, integer, varchar, varchar, uuid);
DROP FUNCTION IF EXISTS procsongupdate(uuid, varchar, text, text, integer, integer);
DROP FUNCTION IF EXISTS procsonggetbyid(uuid, uuid);
DROP FUNCTION IF EXISTS procsonggetall(uuid);
DROP FUNCTION IF EXISTS procsongsearch(varchar, uuid);
DROP FUNCTION IF EXISTS procsonggetbyuploader(uuid, integer, integer);
DROP FUNCTION IF EXISTS procsongvoteset(uuid, uuid, integer);
DROP FUNCTION IF EXISTS procplaylistsonggetbyplaylistid(uuid);

-- procSongInsert — now carries artist and category
CREATE FUNCTION procsonginsert(
    p_songid              uuid,
    p_songname            varchar,
    p_songurl             text,
    p_imageurl            text    DEFAULT NULL,
    p_durationinseconds   integer DEFAULT NULL,
    p_priority            integer DEFAULT 0,
    p_contenthash         varchar DEFAULT NULL,
    p_sourcekey           varchar DEFAULT NULL,
    p_uploadedbyaccountid uuid    DEFAULT NULL,
    p_artist              varchar DEFAULT NULL,
    p_categoryid          uuid    DEFAULT NULL
)
RETURNS TABLE (songid uuid, songname varchar, songurl text, imageurl text,
               durationinseconds integer, priority integer, isdeleted boolean,
               created timestamp, updated timestamp, contenthash varchar,
               sourcekey varchar, uploadedbyaccountid uuid,
               artist citext, categoryid uuid, category citext)
LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO songs (songid, songname, songurl, imageurl, durationinseconds, priority,
                       isdeleted, created, contenthash, sourcekey, uploadedbyaccountid,
                       artist, categoryid)
    VALUES (p_songid, p_songname, p_songurl, p_imageurl, p_durationinseconds,
            COALESCE(p_priority, 0), false, timezone('utc', now()),
            p_contenthash, p_sourcekey, p_uploadedbyaccountid,
            NULLIF(btrim(COALESCE(p_artist, '')), '')::citext, p_categoryid);

    RETURN QUERY
    SELECT s.songid, s.songname, s.songurl, s.imageurl, s.durationinseconds, s.priority,
           s.isdeleted, s.created, s.updated, s.contenthash, s.sourcekey, s.uploadedbyaccountid,
           s.artist, s.categoryid, c.name
    FROM songs s LEFT JOIN category c ON c.categoryid = s.categoryid
    WHERE s.songid = p_songid;
END;
$$;

-- procSongUpdate — artist and category are editable
CREATE FUNCTION procsongupdate(
    p_songid            uuid,
    p_songname          varchar,
    p_songurl           text,
    p_imageurl          text,
    p_durationinseconds integer,
    p_priority          integer,
    p_artist            varchar DEFAULT NULL,
    p_categoryid        uuid    DEFAULT NULL
)
RETURNS TABLE (songid uuid, songname varchar, songurl text, imageurl text,
               durationinseconds integer, priority integer, isdeleted boolean,
               created timestamp, updated timestamp, contenthash varchar,
               sourcekey varchar, uploadedbyaccountid uuid,
               artist citext, categoryid uuid, category citext)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM songs s WHERE s.songid = p_songid AND s.isdeleted = false) THEN
        RAISE EXCEPTION 'Song not found.' USING ERRCODE = '50020';
    END IF;

    UPDATE songs s
    SET songname = p_songname,
        songurl = p_songurl,
        imageurl = p_imageurl,
        durationinseconds = p_durationinseconds,
        priority = p_priority,
        artist = NULLIF(btrim(COALESCE(p_artist, '')), '')::citext,
        categoryid = COALESCE(p_categoryid, s.categoryid),
        updated = timezone('utc', now())
    WHERE s.songid = p_songid;

    RETURN QUERY
    SELECT s.songid, s.songname, s.songurl, s.imageurl, s.durationinseconds, s.priority,
           s.isdeleted, s.created, s.updated, s.contenthash, s.sourcekey, s.uploadedbyaccountid,
           s.artist, s.categoryid, c.name
    FROM songs s LEFT JOIN category c ON c.categoryid = s.categoryid
    WHERE s.songid = p_songid;
END;
$$;

-- procSongGetById — lyrics come back here only; list endpoints stay lean.
CREATE FUNCTION procsonggetbyid(p_songid uuid, p_accountid uuid DEFAULT NULL)
RETURNS TABLE (songid uuid, songname varchar, songurl text, imageurl text,
               durationinseconds integer, priority integer, created timestamp,
               updated timestamp, uploadedbyaccountid uuid, myvote integer,
               artist citext, categoryid uuid, category citext,
               lyrics text, lyricssynced text)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT s.songid, s.songname, s.songurl, s.imageurl, s.durationinseconds, s.priority,
           s.created, s.updated, s.uploadedbyaccountid, COALESCE(v.value, 0),
           s.artist, s.categoryid, c.name, s.lyrics, s.lyricssynced
    FROM songs s
    LEFT JOIN songpriority v ON v.songid = s.songid AND v.accountid = p_accountid
    LEFT JOIN category c ON c.categoryid = s.categoryid
    WHERE s.songid = p_songid AND s.isdeleted = false;
END;
$$;

CREATE FUNCTION procsonggetall(p_accountid uuid DEFAULT NULL)
RETURNS TABLE (songid uuid, songname varchar, songurl text, imageurl text,
               durationinseconds integer, priority integer, created timestamp,
               uploadedbyaccountid uuid, myvote integer,
               artist citext, categoryid uuid, category citext)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT s.songid, s.songname, s.songurl, s.imageurl, s.durationinseconds, s.priority,
           s.created, s.uploadedbyaccountid, COALESCE(v.value, 0),
           s.artist, s.categoryid, c.name
    FROM songs s
    LEFT JOIN songpriority v ON v.songid = s.songid AND v.accountid = p_accountid
    LEFT JOIN category c ON c.categoryid = s.categoryid
    WHERE s.isdeleted = false
    ORDER BY s.priority DESC, s.songname ASC;
END;
$$;

-- Search now also matches the artist, so "arijit" finds their songs.
CREATE FUNCTION procsongsearch(p_searchtext varchar, p_accountid uuid DEFAULT NULL)
RETURNS TABLE (songid uuid, songname varchar, songurl text, imageurl text,
               durationinseconds integer, priority integer, created timestamp,
               uploadedbyaccountid uuid, myvote integer,
               artist citext, categoryid uuid, category citext)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT s.songid, s.songname, s.songurl, s.imageurl, s.durationinseconds, s.priority,
           s.created, s.uploadedbyaccountid, COALESCE(v.value, 0),
           s.artist, s.categoryid, c.name
    FROM songs s
    LEFT JOIN songpriority v ON v.songid = s.songid AND v.accountid = p_accountid
    LEFT JOIN category c ON c.categoryid = s.categoryid
    WHERE s.isdeleted = false
      AND (s.songname ILIKE '%' || COALESCE(p_searchtext, '') || '%'
        OR s.artist   ILIKE '%' || COALESCE(p_searchtext, '') || '%')
    ORDER BY s.priority DESC, s.songname ASC;
END;
$$;

CREATE FUNCTION procsonggetbyuploader(
    p_accountid uuid,
    p_offset    integer DEFAULT 0,
    p_pagesize  integer DEFAULT 10
)
RETURNS TABLE (songid uuid, songname varchar, songurl text, imageurl text,
               durationinseconds integer, priority integer, created timestamp,
               uploadedbyaccountid uuid, myvote integer,
               artist citext, categoryid uuid, category citext)
LANGUAGE plpgsql AS $$
DECLARE
    v_offset   integer := GREATEST(COALESCE(p_offset, 0), 0);
    v_pagesize integer := COALESCE(p_pagesize, 10);
BEGIN
    IF v_pagesize < 1   THEN v_pagesize := 10;  END IF;
    IF v_pagesize > 100 THEN v_pagesize := 100; END IF;

    RETURN QUERY
    SELECT s.songid, s.songname, s.songurl, s.imageurl, s.durationinseconds, s.priority,
           s.created, s.uploadedbyaccountid, COALESCE(v.value, 0),
           s.artist, s.categoryid, c.name
    FROM songs s
    LEFT JOIN songpriority v ON v.songid = s.songid AND v.accountid = p_accountid
    LEFT JOIN category c ON c.categoryid = s.categoryid
    WHERE s.isdeleted = false AND s.uploadedbyaccountid = p_accountid
    ORDER BY s.created DESC
    OFFSET v_offset LIMIT v_pagesize;
END;
$$;

CREATE FUNCTION procsongvoteset(p_accountid uuid, p_songid uuid, p_value integer)
RETURNS TABLE (songid uuid, songname varchar, songurl text, imageurl text,
               durationinseconds integer, priority integer, created timestamp,
               updated timestamp, myvote integer,
               artist citext, categoryid uuid, category citext)
LANGUAGE plpgsql AS $$
DECLARE
    v_value integer;
BEGIN
    v_value := CASE WHEN p_value > 0 THEN 1 WHEN p_value < 0 THEN -1 ELSE 0 END;

    IF NOT EXISTS (SELECT 1 FROM songs s WHERE s.songid = p_songid AND s.isdeleted = false) THEN
        RAISE EXCEPTION 'Song not found.' USING ERRCODE = '50020';
    END IF;

    IF v_value = 0 THEN
        DELETE FROM songpriority sp
        WHERE sp.songid = p_songid AND sp.accountid = p_accountid;
    ELSE
        UPDATE songpriority sp
        SET value = v_value, updated = timezone('utc', now())
        WHERE sp.songid = p_songid AND sp.accountid = p_accountid;

        IF NOT FOUND THEN
            INSERT INTO songpriority (songpriorityid, songid, accountid, value, created)
            VALUES (gen_random_uuid(), p_songid, p_accountid, v_value, timezone('utc', now()));
        END IF;
    END IF;

    UPDATE songs s
    SET priority = COALESCE((SELECT SUM(sp.value) FROM songpriority sp WHERE sp.songid = p_songid), 0),
        updated  = timezone('utc', now())
    WHERE s.songid = p_songid;

    RETURN QUERY
    SELECT s.songid, s.songname, s.songurl, s.imageurl, s.durationinseconds, s.priority,
           s.created, s.updated, COALESCE(v.value, 0),
           s.artist, s.categoryid, c.name
    FROM songs s
    LEFT JOIN songpriority v ON v.songid = s.songid AND v.accountid = p_accountid
    LEFT JOIN category c ON c.categoryid = s.categoryid
    WHERE s.songid = p_songid;
END;
$$;

CREATE FUNCTION procplaylistsonggetbyplaylistid(p_playlistid uuid)
RETURNS TABLE (playlistsongid uuid, songid uuid, songname varchar, songurl text,
               imageurl text, durationinseconds integer, priority integer, addedon timestamp,
               artist citext, categoryid uuid, category citext)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM playlists p WHERE p.playlistid = p_playlistid) THEN
        RAISE EXCEPTION 'Playlist does not exist.' USING ERRCODE = '50044';
    END IF;

    RETURN QUERY
    SELECT ps.playlistsongid, s.songid, s.songname, s.songurl, s.imageurl,
           s.durationinseconds, s.priority, ps.created,
           s.artist, s.categoryid, c.name
    FROM playlistsongs ps
    INNER JOIN songs s ON ps.songid = s.songid
    LEFT JOIN category c ON c.categoryid = s.categoryid
    WHERE ps.playlistid = p_playlistid AND s.isdeleted = false
    ORDER BY s.priority ASC, s.songname ASC;
END;
$$;

-- =====================================================================
--  Lyrics
-- =====================================================================

-- Stores whatever the lyrics provider returned. Called separately from insert so a
-- lyrics lookup can never fail an upload — the song is saved first, lyrics arrive after.
CREATE OR REPLACE FUNCTION procsongsetlyrics(
    p_songid       uuid,
    p_lyrics       text DEFAULT NULL,
    p_lyricssynced text DEFAULT NULL
)
RETURNS TABLE (success boolean, message text)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM songs s WHERE s.songid = p_songid) THEN
        RAISE EXCEPTION 'Song not found.' USING ERRCODE = '50021';
    END IF;

    UPDATE songs s
    SET lyrics = NULLIF(btrim(COALESCE(p_lyrics, '')), ''),
        lyricssynced = NULLIF(btrim(COALESCE(p_lyricssynced, '')), ''),
        updated = timezone('utc', now())
    WHERE s.songid = p_songid;

    RETURN QUERY SELECT true, 'Lyrics updated.'::text;
END;
$$;

-- Seed the fallback category so it always exists and has a stable id.
SELECT proccategorygetorcreate('Uncategorized');

COMMIT;
