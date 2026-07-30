/* =====================================================================
   Migration: duplicate-prevention + priority voting
   Date: 2026-07-24
   Safe to re-run (idempotent). Target DB: MusicDatabase (SQL Server).

   Adds:
     1. Songs.ContentHash  — SHA-256 of an uploaded audio file (exact-dup guard)
     2. Songs.SourceKey    — external source id, e.g. 'youtube:<videoId>' (one-import guard)
     3. Unique filtered indexes on both (only enforced when NOT NULL)
     4. SongPriority table  — one up/down vote per (Song, Account)
     5. Procs: procSongInsert (+2 params), procSongFindDuplicate, procSongVoteSet,
        procSongGetAll / procSongSearch / procSongGetById (+@AccountId, +MyVote, rank DESC)
   ===================================================================== */

SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;   -- required to create filtered indexes below
SET ANSI_NULLS ON;
GO

/* 1 + 2. New columns on Songs -------------------------------------- */
IF COL_LENGTH('dbo.Songs', 'ContentHash') IS NULL
    ALTER TABLE dbo.Songs ADD ContentHash NVARCHAR(64) NULL;
GO
IF COL_LENGTH('dbo.Songs', 'SourceKey') IS NULL
    ALTER TABLE dbo.Songs ADD SourceKey NVARCHAR(100) NULL;
GO

/* 3. Unique filtered indexes (dupes rejected only among live rows) -- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Songs_ContentHash' AND object_id = OBJECT_ID('dbo.Songs'))
    CREATE UNIQUE INDEX UX_Songs_ContentHash ON dbo.Songs(ContentHash)
        WHERE ContentHash IS NOT NULL AND IsDeleted = 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Songs_SourceKey' AND object_id = OBJECT_ID('dbo.Songs'))
    CREATE UNIQUE INDEX UX_Songs_SourceKey ON dbo.Songs(SourceKey)
        WHERE SourceKey IS NOT NULL AND IsDeleted = 0;
GO

/* 4. SongPriority — the "collection" of votes ---------------------- */
IF OBJECT_ID('dbo.SongPriority', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SongPriority
    (
        SongPriorityId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_SongPriority PRIMARY KEY,
        SongId         UNIQUEIDENTIFIER NOT NULL,
        AccountId      UNIQUEIDENTIFIER NOT NULL,
        Value          INT              NOT NULL CONSTRAINT CK_SongPriority_Value CHECK (Value IN (-1, 1)),
        Created        DATETIME2        NOT NULL CONSTRAINT DF_SongPriority_Created DEFAULT (GETUTCDATE()),
        Updated        DATETIME2        NULL,
        CONSTRAINT FK_SongPriority_Song    FOREIGN KEY (SongId)    REFERENCES dbo.Songs(SongId),
        CONSTRAINT FK_SongPriority_Account FOREIGN KEY (AccountId) REFERENCES dbo.Account(AccountId),
        CONSTRAINT UX_SongPriority_Song_Account UNIQUE (SongId, AccountId)  -- one value per user per song
    );
END
GO

/* 5. procSongInsert — carry ContentHash + SourceKey ---------------- */
CREATE OR ALTER PROCEDURE procSongInsert
(
    @SongId UNIQUEIDENTIFIER,
    @SongName NVARCHAR(250),
    @SongUrl NVARCHAR(MAX),
    @ImageUrl NVARCHAR(MAX) = NULL,
    @DurationInSeconds INT = NULL,
    @Priority INT = 0,
    @ContentHash NVARCHAR(64) = NULL,
    @SourceKey NVARCHAR(100) = NULL
)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO Songs
            (SongId, SongName, SongUrl, ImageUrl, DurationInSeconds, Priority, IsDeleted, Created, ContentHash, SourceKey)
        VALUES
            (@SongId, @SongName, @SongUrl, @ImageUrl, @DurationInSeconds, @Priority, 0, GETUTCDATE(), @ContentHash, @SourceKey);

        COMMIT TRANSACTION;

        SELECT * FROM Songs WHERE SongId = @SongId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* procSongFindDuplicate — returns an existing live song by hash or source key */
CREATE OR ALTER PROCEDURE procSongFindDuplicate
(
    @ContentHash NVARCHAR(64) = NULL,
    @SourceKey NVARCHAR(100) = NULL
)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP 1 SongId, SongName, SongUrl, ImageUrl, DurationInSeconds, Priority, Created, Updated
    FROM Songs
    WHERE IsDeleted = 0
      AND (
            (@ContentHash IS NOT NULL AND ContentHash = @ContentHash)
         OR (@SourceKey  IS NOT NULL AND SourceKey  = @SourceKey)
          );
END
GO

/* procSongVoteSet — upsert one user's vote, recompute the song's total */
CREATE OR ALTER PROCEDURE procSongVoteSet
(
    @AccountId UNIQUEIDENTIFIER,
    @SongId UNIQUEIDENTIFIER,
    @Value INT
)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Normalise to -1 / 0 / 1 (0 clears the user's vote)
    SET @Value = CASE WHEN @Value > 0 THEN 1 WHEN @Value < 0 THEN -1 ELSE 0 END;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1 FROM Songs WHERE SongId = @SongId AND IsDeleted = 0)
            THROW 50020, 'Song not found.', 1;

        IF @Value = 0
            DELETE FROM SongPriority WHERE SongId = @SongId AND AccountId = @AccountId;
        ELSE
        BEGIN
            UPDATE SongPriority
                SET Value = @Value, Updated = GETUTCDATE()
                WHERE SongId = @SongId AND AccountId = @AccountId;

            IF @@ROWCOUNT = 0
                INSERT INTO SongPriority (SongPriorityId, SongId, AccountId, Value, Created)
                VALUES (NEWID(), @SongId, @AccountId, @Value, GETUTCDATE());
        END

        UPDATE Songs
            SET Priority = ISNULL((SELECT SUM(Value) FROM SongPriority WHERE SongId = @SongId), 0),
                Updated  = GETUTCDATE()
            WHERE SongId = @SongId;

        COMMIT TRANSACTION;

        SELECT s.SongId, s.SongName, s.SongUrl, s.ImageUrl, s.DurationInSeconds, s.Priority, s.Created, s.Updated,
               ISNULL(v.Value, 0) AS MyVote
        FROM Songs s
        LEFT JOIN SongPriority v ON v.SongId = s.SongId AND v.AccountId = @AccountId
        WHERE s.SongId = @SongId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* procSongGetAll — rank by Priority DESC, include caller's own vote -- */
CREATE OR ALTER PROCEDURE procSongGetAll
(
    @AccountId UNIQUEIDENTIFIER = NULL
)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT s.SongId, s.SongName, s.SongUrl, s.ImageUrl, s.DurationInSeconds, s.Priority, s.Created,
           ISNULL(v.Value, 0) AS MyVote
    FROM Songs s
    LEFT JOIN SongPriority v ON v.SongId = s.SongId AND v.AccountId = @AccountId
    WHERE s.IsDeleted = 0
    ORDER BY s.Priority DESC, s.SongName ASC;
END
GO

/* procSongSearch — same ranking + own vote ------------------------- */
CREATE OR ALTER PROCEDURE procSongSearch
(
    @SearchText NVARCHAR(250),
    @AccountId UNIQUEIDENTIFIER = NULL
)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT s.SongId, s.SongName, s.SongUrl, s.ImageUrl, s.DurationInSeconds, s.Priority, s.Created,
           ISNULL(v.Value, 0) AS MyVote
    FROM Songs s
    LEFT JOIN SongPriority v ON v.SongId = s.SongId AND v.AccountId = @AccountId
    WHERE s.IsDeleted = 0
      AND s.SongName LIKE '%' + @SearchText + '%'
    ORDER BY s.Priority DESC, s.SongName ASC;
END
GO

/* procSongGetById — include caller's own vote ---------------------- */
CREATE OR ALTER PROCEDURE procSongGetById
(
    @SongId UNIQUEIDENTIFIER,
    @AccountId UNIQUEIDENTIFIER = NULL
)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT s.SongId, s.SongName, s.SongUrl, s.ImageUrl, s.DurationInSeconds, s.Priority, s.Created, s.Updated,
           ISNULL(v.Value, 0) AS MyVote
    FROM Songs s
    LEFT JOIN SongPriority v ON v.SongId = s.SongId AND v.AccountId = @AccountId
    WHERE s.SongId = @SongId
      AND s.IsDeleted = 0;
END
GO
