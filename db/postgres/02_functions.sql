-- =====================================================================
--  Sangeet — PostgreSQL functions (port of the 32 SQL Server stored procedures)
--  Run AFTER 01_schema.sql. Idempotent (CREATE OR REPLACE).
--
--  Error handling: SQL Server used `THROW 500xx, 'message', 1`. Here the same numbers
--  become custom SQLSTATEs — `RAISE EXCEPTION '...' USING ERRCODE = '50001'` — so
--  PostgresErrorTranslator.cs can map them to the same HTTP statuses as before.
--
--  Parameters are named p_<lowercase C# property> because RepositoryBase calls every
--  function with PostgreSQL named-argument notation:
--      SELECT * FROM procaccountinsert(p_accountid => @AccountId, ...)
--  Renaming a parameter here breaks that call — keep them in sync with the repositories.
--
--  No BEGIN/COMMIT: a PL/pgSQL function already runs inside one transaction, and
--  RAISE EXCEPTION rolls the whole thing back. That matches the old BEGIN TRAN/CATCH/ROLLBACK.
-- =====================================================================

-- =====================================================================
--  ACCOUNT
-- =====================================================================

-- procAccountInsert — create an account (role forced to User/Admin)
CREATE OR REPLACE FUNCTION procaccountinsert(
    p_accountid    uuid,
    p_email        varchar,
    p_passwordhash text,
    p_role         varchar DEFAULT 'User'
)
RETURNS TABLE (
    accountid uuid, email citext, passwordhash text, isactive boolean,
    created timestamp, updated timestamp, role varchar
)
LANGUAGE plpgsql AS $$
DECLARE
    v_role varchar(20);
BEGIN
    IF EXISTS (SELECT 1 FROM account a WHERE a.email = p_email::citext) THEN
        RAISE EXCEPTION 'Email already exists.' USING ERRCODE = '50001';
    END IF;

    -- The app never grants SuperAdmin; force anything unexpected down to User.
    v_role := CASE
                WHEN lower(p_role) = 'admin' THEN 'Admin'
                WHEN lower(p_role) = 'user'  THEN 'User'
                ELSE 'User'
              END;

    INSERT INTO account (accountid, email, passwordhash, isactive, created, role)
    VALUES (p_accountid, p_email::citext, p_passwordhash, true, timezone('utc', now()), v_role);

    RETURN QUERY
    SELECT a.accountid, a.email, a.passwordhash, a.isactive, a.created, a.updated, a.role
    FROM account a WHERE a.accountid = p_accountid;
END;
$$;

-- procAccountLogin — credentials lookup (active accounts only)
CREATE OR REPLACE FUNCTION procaccountlogin(p_email varchar)
RETURNS TABLE (accountid uuid, email citext, passwordhash text, isactive boolean, role varchar)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT a.accountid, a.email, a.passwordhash, a.isactive, a.role
    FROM account a
    WHERE a.email = p_email::citext AND a.isactive = true;
END;
$$;

-- procAccountGetById
CREATE OR REPLACE FUNCTION procaccountgetbyid(p_accountid uuid)
RETURNS TABLE (accountid uuid, email citext, isactive boolean, role varchar,
               created timestamp, updated timestamp)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT a.accountid, a.email, a.isactive, a.role, a.created, a.updated
    FROM account a WHERE a.accountid = p_accountid;
END;
$$;

-- procAccountUpdate
CREATE OR REPLACE FUNCTION procaccountupdate(
    p_accountid uuid,
    p_email     varchar,
    p_isactive  boolean
)
RETURNS TABLE (
    accountid uuid, email citext, passwordhash text, isactive boolean,
    created timestamp, updated timestamp, role varchar
)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM account a WHERE a.accountid = p_accountid) THEN
        RAISE EXCEPTION 'Account not found.' USING ERRCODE = '50002';
    END IF;

    UPDATE account a
    SET email = p_email::citext, isactive = p_isactive, updated = timezone('utc', now())
    WHERE a.accountid = p_accountid;

    RETURN QUERY
    SELECT a.accountid, a.email, a.passwordhash, a.isactive, a.created, a.updated, a.role
    FROM account a WHERE a.accountid = p_accountid;
END;
$$;

-- procAccountDelete
CREATE OR REPLACE FUNCTION procaccountdelete(p_accountid uuid)
RETURNS TABLE (success boolean, message text)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM account a WHERE a.accountid = p_accountid) THEN
        RAISE EXCEPTION 'Account not found.' USING ERRCODE = '50003';
    END IF;

    DELETE FROM account a WHERE a.accountid = p_accountid;

    RETURN QUERY SELECT true, 'Account Deleted Successfully.'::text;
END;
$$;

-- procAccountGetAllWithRole — SuperAdmin user directory
CREATE OR REPLACE FUNCTION procaccountgetallwithrole()
RETURNS TABLE (accountid uuid, email citext, role varchar, isactive boolean, created timestamp,
               userid uuid, username citext, fullname varchar)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT a.accountid, a.email, a.role, a.isactive, a.created,
           u.userid, u.username, u.fullname
    FROM account a
    LEFT JOIN users u ON u.accountid = a.accountid
    ORDER BY a.created DESC;
END;
$$;

-- procAccountSetRole — SuperAdmin sets User/Admin (never SuperAdmin)
CREATE OR REPLACE FUNCTION procaccountsetrole(p_accountid uuid, p_role varchar)
RETURNS TABLE (accountid uuid, email citext, isactive boolean, role varchar,
               created timestamp, updated timestamp)
LANGUAGE plpgsql AS $$
DECLARE
    v_role varchar(20);
BEGIN
    IF NOT EXISTS (SELECT 1 FROM account a WHERE a.accountid = p_accountid) THEN
        RAISE EXCEPTION 'Account not found.' USING ERRCODE = '50003';
    END IF;

    v_role := CASE
                WHEN lower(p_role) = 'admin' THEN 'Admin'
                WHEN lower(p_role) = 'user'  THEN 'User'
                ELSE NULL
              END;

    IF v_role IS NULL THEN
        RAISE EXCEPTION 'Role must be User or Admin.' USING ERRCODE = '50004';
    END IF;

    UPDATE account a
    SET role = v_role, updated = timezone('utc', now())
    WHERE a.accountid = p_accountid;

    RETURN QUERY
    SELECT a.accountid, a.email, a.isactive, a.role, a.created, a.updated
    FROM account a WHERE a.accountid = p_accountid;
END;
$$;

-- procAccountCascadeDelete — SuperAdmin removes an account + its data
CREATE OR REPLACE FUNCTION procaccountcascadedelete(p_accountid uuid)
RETURNS TABLE (success boolean, message text)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM account a WHERE a.accountid = p_accountid) THEN
        RAISE EXCEPTION 'Account not found.' USING ERRCODE = '50003';
    END IF;

    DELETE FROM songpriority sp WHERE sp.accountid = p_accountid;             -- their votes
    DELETE FROM playlistsongs ps
        WHERE ps.playlistid IN (SELECT p.playlistid FROM playlists p WHERE p.accountid = p_accountid);
    DELETE FROM playlists p WHERE p.accountid = p_accountid;                   -- their playlists
    DELETE FROM users u WHERE u.accountid = p_accountid;                       -- their profile
    DELETE FROM account a WHERE a.accountid = p_accountid;                     -- the account
    -- Songs they uploaded stay (global library); uploadedbyaccountid simply dangles.

    RETURN QUERY SELECT true, 'Account and its data deleted.'::text;
END;
$$;

-- =====================================================================
--  USERS
-- =====================================================================

-- procUserInsert
CREATE OR REPLACE FUNCTION procuserinsert(
    p_userid          uuid,
    p_accountid       uuid,
    p_username        varchar,
    p_fullname        varchar,
    p_profileimageurl text DEFAULT NULL
)
RETURNS TABLE (userid uuid, accountid uuid, username citext, fullname varchar,
               profileimageurl text, created timestamp, updated timestamp)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM account a WHERE a.accountid = p_accountid) THEN
        RAISE EXCEPTION 'Account does not exist.' USING ERRCODE = '50010';
    END IF;

    IF EXISTS (SELECT 1 FROM users u WHERE u.username = p_username::citext) THEN
        RAISE EXCEPTION 'Username already exists.' USING ERRCODE = '50011';
    END IF;

    INSERT INTO users (userid, accountid, username, fullname, profileimageurl, created)
    VALUES (p_userid, p_accountid, p_username::citext, p_fullname, p_profileimageurl,
            timezone('utc', now()));

    RETURN QUERY
    SELECT u.userid, u.accountid, u.username, u.fullname, u.profileimageurl, u.created, u.updated
    FROM users u WHERE u.userid = p_userid;
END;
$$;

-- procUserUpdate
CREATE OR REPLACE FUNCTION procuserupdate(
    p_userid          uuid,
    p_fullname        varchar,
    p_profileimageurl text,
    p_username        varchar
)
RETURNS TABLE (userid uuid, accountid uuid, username citext, fullname varchar,
               profileimageurl text, created timestamp, updated timestamp)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM users u WHERE u.userid = p_userid) THEN
        RAISE EXCEPTION 'User not found.' USING ERRCODE = '50012';
    END IF;

    IF EXISTS (SELECT 1 FROM users u WHERE u.username = p_username::citext AND u.userid <> p_userid) THEN
        RAISE EXCEPTION 'Username already exists.' USING ERRCODE = '50013';
    END IF;

    UPDATE users u
    SET fullname = p_fullname,
        username = p_username::citext,
        profileimageurl = p_profileimageurl,
        updated = timezone('utc', now())
    WHERE u.userid = p_userid;

    RETURN QUERY
    SELECT u.userid, u.accountid, u.username, u.fullname, u.profileimageurl, u.created, u.updated
    FROM users u WHERE u.userid = p_userid;
END;
$$;

-- procUserGetById
CREATE OR REPLACE FUNCTION procusergetbyid(p_userid uuid)
RETURNS TABLE (userid uuid, accountid uuid, username citext, fullname varchar,
               profileimageurl text, created timestamp, updated timestamp,
               email citext, role varchar)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT u.userid, u.accountid, u.username, u.fullname, u.profileimageurl, u.created, u.updated,
           a.email, a.role
    FROM users u INNER JOIN account a ON u.accountid = a.accountid
    WHERE u.userid = p_userid;
END;
$$;

-- procUserGetByAccountId
CREATE OR REPLACE FUNCTION procusergetbyaccountid(p_accountid uuid)
RETURNS TABLE (userid uuid, accountid uuid, username citext, fullname varchar,
               profileimageurl text, created timestamp, updated timestamp,
               email citext, role varchar)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT u.userid, u.accountid, u.username, u.fullname, u.profileimageurl, u.created, u.updated,
           a.email, a.role
    FROM users u INNER JOIN account a ON u.accountid = a.accountid
    WHERE u.accountid = p_accountid;
END;
$$;

-- procUserGetByUserName
CREATE OR REPLACE FUNCTION procusergetbyusername(p_username varchar)
RETURNS TABLE (userid uuid, accountid uuid, username citext, fullname varchar,
               profileimageurl text, email citext)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT u.userid, u.accountid, u.username, u.fullname, u.profileimageurl, a.email
    FROM users u INNER JOIN account a ON u.accountid = a.accountid
    WHERE u.username = p_username::citext;
END;
$$;

-- procUserGetAll
CREATE OR REPLACE FUNCTION procusergetall()
RETURNS TABLE (userid uuid, username citext, fullname varchar, profileimageurl text,
               email citext, created timestamp)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT u.userid, u.username, u.fullname, u.profileimageurl, a.email, u.created
    FROM users u INNER JOIN account a ON u.accountid = a.accountid
    ORDER BY u.created DESC;
END;
$$;

-- =====================================================================
--  SONGS
-- =====================================================================

-- procSongInsert — carries the uploader id, ContentHash and SourceKey
CREATE OR REPLACE FUNCTION procsonginsert(
    p_songid              uuid,
    p_songname            varchar,
    p_songurl             text,
    p_imageurl            text    DEFAULT NULL,
    p_durationinseconds   integer DEFAULT NULL,
    p_priority            integer DEFAULT 0,
    p_contenthash         varchar DEFAULT NULL,
    p_sourcekey           varchar DEFAULT NULL,
    p_uploadedbyaccountid uuid    DEFAULT NULL
)
RETURNS TABLE (songid uuid, songname varchar, songurl text, imageurl text,
               durationinseconds integer, priority integer, isdeleted boolean,
               created timestamp, updated timestamp, contenthash varchar,
               sourcekey varchar, uploadedbyaccountid uuid)
LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO songs (songid, songname, songurl, imageurl, durationinseconds, priority,
                       isdeleted, created, contenthash, sourcekey, uploadedbyaccountid)
    VALUES (p_songid, p_songname, p_songurl, p_imageurl, p_durationinseconds,
            COALESCE(p_priority, 0), false, timezone('utc', now()),
            p_contenthash, p_sourcekey, p_uploadedbyaccountid);

    RETURN QUERY
    SELECT s.songid, s.songname, s.songurl, s.imageurl, s.durationinseconds, s.priority,
           s.isdeleted, s.created, s.updated, s.contenthash, s.sourcekey, s.uploadedbyaccountid
    FROM songs s WHERE s.songid = p_songid;
END;
$$;

-- procSongUpdate
CREATE OR REPLACE FUNCTION procsongupdate(
    p_songid            uuid,
    p_songname          varchar,
    p_songurl           text,
    p_imageurl          text,
    p_durationinseconds integer,
    p_priority          integer
)
RETURNS TABLE (songid uuid, songname varchar, songurl text, imageurl text,
               durationinseconds integer, priority integer, isdeleted boolean,
               created timestamp, updated timestamp, contenthash varchar,
               sourcekey varchar, uploadedbyaccountid uuid)
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
        updated = timezone('utc', now())
    WHERE s.songid = p_songid;

    RETURN QUERY
    SELECT s.songid, s.songname, s.songurl, s.imageurl, s.durationinseconds, s.priority,
           s.isdeleted, s.created, s.updated, s.contenthash, s.sourcekey, s.uploadedbyaccountid
    FROM songs s WHERE s.songid = p_songid;
END;
$$;

-- procSongDelete — soft delete
CREATE OR REPLACE FUNCTION procsongdelete(p_songid uuid)
RETURNS TABLE (success boolean, message text)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM songs s WHERE s.songid = p_songid AND s.isdeleted = false) THEN
        RAISE EXCEPTION 'Song not found.' USING ERRCODE = '50021';
    END IF;

    UPDATE songs s
    SET isdeleted = true, updated = timezone('utc', now())
    WHERE s.songid = p_songid;

    RETURN QUERY SELECT true, 'Song deleted successfully.'::text;
END;
$$;

-- procSongHardDelete — permanent removal (row + playlist links + votes)
CREATE OR REPLACE FUNCTION procsongharddelete(p_songid uuid)
RETURNS TABLE (success boolean, message text)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM songs s WHERE s.songid = p_songid) THEN
        RAISE EXCEPTION 'Song not found.' USING ERRCODE = '50021';
    END IF;

    DELETE FROM playlistsongs ps WHERE ps.songid = p_songid;   -- remove from every playlist
    DELETE FROM songpriority sp  WHERE sp.songid = p_songid;   -- drop its votes
    DELETE FROM songs s          WHERE s.songid  = p_songid;   -- remove the song itself

    RETURN QUERY SELECT true, 'Song permanently deleted.'::text;
END;
$$;

-- procSongGetById
CREATE OR REPLACE FUNCTION procsonggetbyid(p_songid uuid, p_accountid uuid DEFAULT NULL)
RETURNS TABLE (songid uuid, songname varchar, songurl text, imageurl text,
               durationinseconds integer, priority integer, created timestamp,
               updated timestamp, uploadedbyaccountid uuid, myvote integer)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT s.songid, s.songname, s.songurl, s.imageurl, s.durationinseconds, s.priority,
           s.created, s.updated, s.uploadedbyaccountid, COALESCE(v.value, 0) AS myvote
    FROM songs s
    LEFT JOIN songpriority v ON v.songid = s.songid AND v.accountid = p_accountid
    WHERE s.songid = p_songid AND s.isdeleted = false;
END;
$$;

-- procSongGetAll
CREATE OR REPLACE FUNCTION procsonggetall(p_accountid uuid DEFAULT NULL)
RETURNS TABLE (songid uuid, songname varchar, songurl text, imageurl text,
               durationinseconds integer, priority integer, created timestamp,
               uploadedbyaccountid uuid, myvote integer)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT s.songid, s.songname, s.songurl, s.imageurl, s.durationinseconds, s.priority,
           s.created, s.uploadedbyaccountid, COALESCE(v.value, 0) AS myvote
    FROM songs s
    LEFT JOIN songpriority v ON v.songid = s.songid AND v.accountid = p_accountid
    WHERE s.isdeleted = false
    ORDER BY s.priority DESC, s.songname ASC;
END;
$$;

-- procSongSearch — ILIKE keeps SQL Server's case-insensitive LIKE behaviour
CREATE OR REPLACE FUNCTION procsongsearch(p_searchtext varchar, p_accountid uuid DEFAULT NULL)
RETURNS TABLE (songid uuid, songname varchar, songurl text, imageurl text,
               durationinseconds integer, priority integer, created timestamp,
               uploadedbyaccountid uuid, myvote integer)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT s.songid, s.songname, s.songurl, s.imageurl, s.durationinseconds, s.priority,
           s.created, s.uploadedbyaccountid, COALESCE(v.value, 0) AS myvote
    FROM songs s
    LEFT JOIN songpriority v ON v.songid = s.songid AND v.accountid = p_accountid
    WHERE s.isdeleted = false
      AND s.songname ILIKE '%' || COALESCE(p_searchtext, '') || '%'
    ORDER BY s.priority DESC, s.songname ASC;
END;
$$;

-- procSongFindDuplicate — an existing live song by content hash or source key
CREATE OR REPLACE FUNCTION procsongfindduplicate(
    p_contenthash varchar DEFAULT NULL,
    p_sourcekey   varchar DEFAULT NULL
)
RETURNS TABLE (songid uuid, songname varchar, songurl text, imageurl text,
               durationinseconds integer, priority integer,
               created timestamp, updated timestamp)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT s.songid, s.songname, s.songurl, s.imageurl, s.durationinseconds, s.priority,
           s.created, s.updated
    FROM songs s
    WHERE s.isdeleted = false
      AND (
            (p_contenthash IS NOT NULL AND s.contenthash = p_contenthash)
         OR (p_sourcekey   IS NOT NULL AND s.sourcekey   = p_sourcekey)
          )
    LIMIT 1;
END;
$$;

-- procSongGetByUploader — page of a user's uploads.
-- SQL Server returned TWO result sets (page, then total). PostgreSQL functions return one,
-- so the count lives in the companion function below; RepositoryBase.QueryPageAsync sends
-- both statements in a single command and reads them with Dapper's QueryMultiple.
CREATE OR REPLACE FUNCTION procsonggetbyuploader(
    p_accountid uuid,
    p_offset    integer DEFAULT 0,
    p_pagesize  integer DEFAULT 10
)
RETURNS TABLE (songid uuid, songname varchar, songurl text, imageurl text,
               durationinseconds integer, priority integer, created timestamp,
               uploadedbyaccountid uuid, myvote integer)
LANGUAGE plpgsql AS $$
DECLARE
    v_offset   integer := GREATEST(COALESCE(p_offset, 0), 0);
    v_pagesize integer := COALESCE(p_pagesize, 10);
BEGIN
    IF v_pagesize < 1   THEN v_pagesize := 10;  END IF;
    IF v_pagesize > 100 THEN v_pagesize := 100; END IF;   -- hard cap

    RETURN QUERY
    SELECT s.songid, s.songname, s.songurl, s.imageurl, s.durationinseconds, s.priority,
           s.created, s.uploadedbyaccountid, COALESCE(v.value, 0) AS myvote
    FROM songs s
    LEFT JOIN songpriority v ON v.songid = s.songid AND v.accountid = p_accountid
    WHERE s.isdeleted = false AND s.uploadedbyaccountid = p_accountid
    ORDER BY s.created DESC
    OFFSET v_offset LIMIT v_pagesize;
END;
$$;

-- Companion count for the pager. Takes the same arguments (offset/pagesize unused) so the
-- caller can send one identical argument list to both statements.
CREATE OR REPLACE FUNCTION procsonggetbyuploader_total(
    p_accountid uuid,
    p_offset    integer DEFAULT 0,
    p_pagesize  integer DEFAULT 10
)
RETURNS TABLE (total integer)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT CAST(COUNT(*) AS integer)
    FROM songs s
    WHERE s.isdeleted = false AND s.uploadedbyaccountid = p_accountid;
END;
$$;

-- procSongVoteSet — upsert one user's vote, recompute the song's total priority
CREATE OR REPLACE FUNCTION procsongvoteset(p_accountid uuid, p_songid uuid, p_value integer)
RETURNS TABLE (songid uuid, songname varchar, songurl text, imageurl text,
               durationinseconds integer, priority integer, created timestamp,
               updated timestamp, myvote integer)
LANGUAGE plpgsql AS $$
DECLARE
    v_value integer;
BEGIN
    -- Normalise to -1 / 0 / 1 (0 clears the user's vote)
    v_value := CASE WHEN p_value > 0 THEN 1 WHEN p_value < 0 THEN -1 ELSE 0 END;

    IF NOT EXISTS (SELECT 1 FROM songs s WHERE s.songid = p_songid AND s.isdeleted = false) THEN
        RAISE EXCEPTION 'Song not found.' USING ERRCODE = '50020';
    END IF;

    IF v_value = 0 THEN
        DELETE FROM songpriority sp
        WHERE sp.songid = p_songid AND sp.accountid = p_accountid;
    ELSE
        -- Update-then-insert (the original T-SQL shape) rather than ON CONFLICT: this function
        -- has an OUT column called "songid", and PL/pgSQL would resolve the bare "songid" in an
        -- ON CONFLICT target to that variable instead of the table column.
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
           s.created, s.updated, COALESCE(v.value, 0) AS myvote
    FROM songs s
    LEFT JOIN songpriority v ON v.songid = s.songid AND v.accountid = p_accountid
    WHERE s.songid = p_songid;
END;
$$;

-- =====================================================================
--  PLAYLISTS
-- =====================================================================

-- procPlaylistInsert
CREATE OR REPLACE FUNCTION procplaylistinsert(
    p_playlistid   uuid,
    p_accountid    uuid,
    p_playlistname varchar
)
RETURNS TABLE (playlistid uuid, accountid uuid, playlistname citext,
               created timestamp, updated timestamp)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM account a WHERE a.accountid = p_accountid) THEN
        RAISE EXCEPTION 'Account does not exist.' USING ERRCODE = '50030';
    END IF;

    IF btrim(COALESCE(p_playlistname, '')) = '' THEN
        RAISE EXCEPTION 'Playlist name is required.' USING ERRCODE = '50031';
    END IF;

    IF EXISTS (SELECT 1 FROM playlists p
               WHERE p.accountid = p_accountid AND p.playlistname = p_playlistname::citext) THEN
        RAISE EXCEPTION 'Playlist already exists.' USING ERRCODE = '50032';
    END IF;

    INSERT INTO playlists (playlistid, accountid, playlistname, created)
    VALUES (p_playlistid, p_accountid, p_playlistname::citext, timezone('utc', now()));

    RETURN QUERY
    SELECT p.playlistid, p.accountid, p.playlistname, p.created, p.updated
    FROM playlists p WHERE p.playlistid = p_playlistid;
END;
$$;

-- procPlaylistUpdate
CREATE OR REPLACE FUNCTION procplaylistupdate(p_playlistid uuid, p_playlistname varchar)
RETURNS TABLE (playlistid uuid, accountid uuid, playlistname citext,
               created timestamp, updated timestamp)
LANGUAGE plpgsql AS $$
DECLARE
    v_accountid uuid;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM playlists p WHERE p.playlistid = p_playlistid) THEN
        RAISE EXCEPTION 'Playlist does not exist.' USING ERRCODE = '50033';
    END IF;

    IF btrim(COALESCE(p_playlistname, '')) = '' THEN
        RAISE EXCEPTION 'Playlist name is required.' USING ERRCODE = '50034';
    END IF;

    SELECT p.accountid INTO v_accountid FROM playlists p WHERE p.playlistid = p_playlistid;

    IF EXISTS (SELECT 1 FROM playlists p
               WHERE p.accountid = v_accountid
                 AND p.playlistname = p_playlistname::citext
                 AND p.playlistid <> p_playlistid) THEN
        RAISE EXCEPTION 'Playlist name already exists.' USING ERRCODE = '50035';
    END IF;

    UPDATE playlists p
    SET playlistname = p_playlistname::citext, updated = timezone('utc', now())
    WHERE p.playlistid = p_playlistid;

    RETURN QUERY
    SELECT p.playlistid, p.accountid, p.playlistname, p.created, p.updated
    FROM playlists p WHERE p.playlistid = p_playlistid;
END;
$$;

-- procPlaylistDelete
CREATE OR REPLACE FUNCTION procplaylistdelete(p_playlistid uuid)
RETURNS TABLE (success boolean, message text)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM playlists p WHERE p.playlistid = p_playlistid) THEN
        RAISE EXCEPTION 'Playlist does not exist.' USING ERRCODE = '50036';
    END IF;

    DELETE FROM playlists p WHERE p.playlistid = p_playlistid;

    RETURN QUERY SELECT true, 'Playlist deleted successfully.'::text;
END;
$$;

-- procPlaylistGetById
CREATE OR REPLACE FUNCTION procplaylistgetbyid(p_playlistid uuid)
RETURNS TABLE (playlistid uuid, accountid uuid, playlistname citext,
               created timestamp, updated timestamp, totalsongs integer)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM playlists p WHERE p.playlistid = p_playlistid) THEN
        RAISE EXCEPTION 'Playlist does not exist.' USING ERRCODE = '50037';
    END IF;

    RETURN QUERY
    SELECT p.playlistid, p.accountid, p.playlistname, p.created, p.updated,
           CAST(COUNT(ps.playlistsongid) AS integer) AS totalsongs
    FROM playlists p
    LEFT JOIN playlistsongs ps ON p.playlistid = ps.playlistid
    WHERE p.playlistid = p_playlistid
    GROUP BY p.playlistid, p.accountid, p.playlistname, p.created, p.updated;
END;
$$;

-- procPlaylistGetByAccountId
CREATE OR REPLACE FUNCTION procplaylistgetbyaccountid(p_accountid uuid)
RETURNS TABLE (playlistid uuid, playlistname citext, created timestamp,
               updated timestamp, totalsongs integer)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM account a WHERE a.accountid = p_accountid) THEN
        RAISE EXCEPTION 'Account does not exist.' USING ERRCODE = '50038';
    END IF;

    RETURN QUERY
    SELECT p.playlistid, p.playlistname, p.created, p.updated,
           CAST(COUNT(ps.playlistsongid) AS integer) AS totalsongs
    FROM playlists p
    LEFT JOIN playlistsongs ps ON p.playlistid = ps.playlistid
    WHERE p.accountid = p_accountid
    GROUP BY p.playlistid, p.playlistname, p.created, p.updated
    ORDER BY p.playlistname ASC;
END;
$$;

-- =====================================================================
--  PLAYLIST SONGS
-- =====================================================================

-- procPlaylistSongAdd
CREATE OR REPLACE FUNCTION procplaylistsongadd(
    p_playlistsongid uuid,
    p_playlistid     uuid,
    p_songid         uuid
)
RETURNS TABLE (playlistsongid uuid, playlistid uuid, songid uuid, created timestamp)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM playlists p WHERE p.playlistid = p_playlistid) THEN
        RAISE EXCEPTION 'Playlist does not exist.' USING ERRCODE = '50040';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM songs s WHERE s.songid = p_songid AND s.isdeleted = false) THEN
        RAISE EXCEPTION 'Song does not exist.' USING ERRCODE = '50041';
    END IF;

    IF EXISTS (SELECT 1 FROM playlistsongs ps
               WHERE ps.playlistid = p_playlistid AND ps.songid = p_songid) THEN
        RAISE EXCEPTION 'Song already exists in playlist.' USING ERRCODE = '50042';
    END IF;

    INSERT INTO playlistsongs (playlistsongid, playlistid, songid, created)
    VALUES (p_playlistsongid, p_playlistid, p_songid, timezone('utc', now()));

    RETURN QUERY
    SELECT ps.playlistsongid, ps.playlistid, ps.songid, ps.created
    FROM playlistsongs ps WHERE ps.playlistsongid = p_playlistsongid;
END;
$$;

-- procPlaylistSongRemove
CREATE OR REPLACE FUNCTION procplaylistsongremove(p_playlistid uuid, p_songid uuid)
RETURNS TABLE (success boolean, message text)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM playlistsongs ps
                   WHERE ps.playlistid = p_playlistid AND ps.songid = p_songid) THEN
        RAISE EXCEPTION 'Song does not exist in this playlist.' USING ERRCODE = '50043';
    END IF;

    DELETE FROM playlistsongs ps
    WHERE ps.playlistid = p_playlistid AND ps.songid = p_songid;

    RETURN QUERY SELECT true, 'Song removed successfully from playlist.'::text;
END;
$$;

-- procPlaylistSongGetByPlaylistId
CREATE OR REPLACE FUNCTION procplaylistsonggetbyplaylistid(p_playlistid uuid)
RETURNS TABLE (playlistsongid uuid, songid uuid, songname varchar, songurl text,
               imageurl text, durationinseconds integer, priority integer, addedon timestamp)
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM playlists p WHERE p.playlistid = p_playlistid) THEN
        RAISE EXCEPTION 'Playlist does not exist.' USING ERRCODE = '50044';
    END IF;

    RETURN QUERY
    SELECT ps.playlistsongid, s.songid, s.songname, s.songurl, s.imageurl,
           s.durationinseconds, s.priority, ps.created AS addedon
    FROM playlistsongs ps
    INNER JOIN songs s ON ps.songid = s.songid
    WHERE ps.playlistid = p_playlistid AND s.isdeleted = false
    ORDER BY s.priority ASC, s.songname ASC;
END;
$$;
