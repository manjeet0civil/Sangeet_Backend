/* =====================================================================
   Migration: "My uploads" paginated history
   Date: 2026-07-24  (run AFTER the earlier migrations)
   Idempotent. Run with:  SQLCMD -S <server> -E -d MusicDatabase -I -i <file>

   Adds procSongGetByUploader — returns one page of the songs a given account uploaded,
   newest first, plus the total count (as a second result set) for pagination.
   ===================================================================== */

SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

CREATE OR ALTER PROCEDURE procSongGetByUploader
(
    @AccountId UNIQUEIDENTIFIER,
    @Offset INT = 0,
    @PageSize INT = 10
)
AS
BEGIN
    SET NOCOUNT ON;

    IF @PageSize < 1 SET @PageSize = 10;
    IF @PageSize > 100 SET @PageSize = 100;   -- hard cap
    IF @Offset < 0 SET @Offset = 0;

    -- Result set 1: the page of songs
    SELECT s.SongId, s.SongName, s.SongUrl, s.ImageUrl, s.DurationInSeconds, s.Priority, s.Created,
           s.UploadedByAccountId, ISNULL(v.Value, 0) AS MyVote
    FROM Songs s
    LEFT JOIN SongPriority v ON v.SongId = s.SongId AND v.AccountId = @AccountId
    WHERE s.IsDeleted = 0 AND s.UploadedByAccountId = @AccountId
    ORDER BY s.Created DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

    -- Result set 2: the total count (for the pager)
    SELECT COUNT(*) AS Total
    FROM Songs
    WHERE IsDeleted = 0 AND UploadedByAccountId = @AccountId;
END
GO
