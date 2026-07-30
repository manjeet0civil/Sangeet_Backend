/* =====================================================================
   Migration: role-based access control + song ownership + hard delete
   Date: 2026-07-24  (run AFTER 2026-07-24_dedup_and_voting.sql)
   Idempotent. Run with:  SQLCMD -S <server> -E -d MusicDatabase -I -i <file>
   (-I = QUOTED_IDENTIFIER ON, required because Songs has filtered indexes.)

   Adds:
     1. Account.Role                 — 'User' | 'Admin' | 'SuperAdmin'
     2. Songs.UploadedByAccountId    — who uploaded/imported the song (owner)
     3. Role in login / profile projections
     4. procSongInsert (+@UploadedByAccountId), song get/search return the owner
     5. procSongHardDelete           — permanently removes a song + its links + votes
     6. procAccountGetAllWithRole / procAccountSetRole / procAccountCascadeDelete  (SuperAdmin)

   NOTE: SuperAdmin is NEVER granted through the app. Grant it by hand, e.g.:
     UPDATE Account SET Role = 'SuperAdmin' WHERE Email = 'you@example.com';
   ===================================================================== */

SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* 1. Account.Role -------------------------------------------------- */
IF COL_LENGTH('dbo.Account', 'Role') IS NULL
    ALTER TABLE dbo.Account ADD Role NVARCHAR(20) NOT NULL CONSTRAINT DF_Account_Role DEFAULT ('User');
GO

/* 2. Songs.UploadedByAccountId (no FK: songs are global & must survive account deletion) */
IF COL_LENGTH('dbo.Songs', 'UploadedByAccountId') IS NULL
    ALTER TABLE dbo.Songs ADD UploadedByAccountId UNIQUEIDENTIFIER NULL;
GO

/* 3a. procAccountLogin — return Role so the JWT can carry it -------- */
CREATE OR ALTER PROCEDURE procAccountLogin
(
    @Email NVARCHAR(255)
)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT AccountId, Email, PasswordHash, IsActive, Role
    FROM Account
    WHERE Email = @Email AND IsActive = 1;
END
GO

/* 3b. procAccountGetById — include Role ---------------------------- */
CREATE OR ALTER PROCEDURE procAccountGetById
(
    @AccountId UNIQUEIDENTIFIER
)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT AccountId, Email, IsActive, Role, Created, Updated
    FROM Account
    WHERE AccountId = @AccountId;
END
GO

/* 3c. procAccountInsert — accept a role (default 'User') ------------ */
CREATE OR ALTER PROCEDURE procAccountInsert
(
    @AccountId UNIQUEIDENTIFIER,
    @Email NVARCHAR(255),
    @PasswordHash NVARCHAR(MAX),
    @Role NVARCHAR(20) = 'User'
)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        IF EXISTS (SELECT 1 FROM Account WHERE Email = @Email)
            THROW 50001, 'Email already exists.', 1;

        -- The app never grants SuperAdmin; force anything unexpected down to User.
        IF @Role NOT IN ('User', 'Admin') SET @Role = 'User';

        BEGIN TRANSACTION;
            INSERT INTO Account (AccountId, Email, PasswordHash, IsActive, Created, Role)
            VALUES (@AccountId, @Email, @PasswordHash, 1, GETUTCDATE(), @Role);
        COMMIT TRANSACTION;

        SELECT * FROM Account WHERE AccountId = @AccountId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* 3d. procUserGetById / procUserGetByAccountId — include A.Role ----- */
CREATE OR ALTER PROCEDURE procUserGetById
(
    @UserId UNIQUEIDENTIFIER
)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT U.UserId, U.AccountId, U.UserName, U.FullName, U.ProfileImageUrl, U.Created, U.Updated, A.Email, A.Role
    FROM Users U INNER JOIN Account A ON U.AccountId = A.AccountId
    WHERE U.UserId = @UserId;
END
GO
CREATE OR ALTER PROCEDURE procUserGetByAccountId
(
    @AccountId UNIQUEIDENTIFIER
)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT U.UserId, U.AccountId, U.UserName, U.FullName, U.ProfileImageUrl, U.Created, U.Updated, A.Email, A.Role
    FROM Users U INNER JOIN Account A ON U.AccountId = A.AccountId
    WHERE U.AccountId = @AccountId;
END
GO

/* 4a. procSongInsert — carry the uploader id (keeps ContentHash/SourceKey) */
CREATE OR ALTER PROCEDURE procSongInsert
(
    @SongId UNIQUEIDENTIFIER,
    @SongName NVARCHAR(250),
    @SongUrl NVARCHAR(MAX),
    @ImageUrl NVARCHAR(MAX) = NULL,
    @DurationInSeconds INT = NULL,
    @Priority INT = 0,
    @ContentHash NVARCHAR(64) = NULL,
    @SourceKey NVARCHAR(100) = NULL,
    @UploadedByAccountId UNIQUEIDENTIFIER = NULL
)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;
        INSERT INTO Songs
            (SongId, SongName, SongUrl, ImageUrl, DurationInSeconds, Priority, IsDeleted, Created, ContentHash, SourceKey, UploadedByAccountId)
        VALUES
            (@SongId, @SongName, @SongUrl, @ImageUrl, @DurationInSeconds, @Priority, 0, GETUTCDATE(), @ContentHash, @SourceKey, @UploadedByAccountId);
        COMMIT TRANSACTION;

        SELECT * FROM Songs WHERE SongId = @SongId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* 4b. Song reads — also return UploadedByAccountId (owner) ---------- */
CREATE OR ALTER PROCEDURE procSongGetById
(
    @SongId UNIQUEIDENTIFIER,
    @AccountId UNIQUEIDENTIFIER = NULL
)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.SongId, s.SongName, s.SongUrl, s.ImageUrl, s.DurationInSeconds, s.Priority, s.Created, s.Updated,
           s.UploadedByAccountId, ISNULL(v.Value, 0) AS MyVote
    FROM Songs s
    LEFT JOIN SongPriority v ON v.SongId = s.SongId AND v.AccountId = @AccountId
    WHERE s.SongId = @SongId AND s.IsDeleted = 0;
END
GO
CREATE OR ALTER PROCEDURE procSongGetAll
(
    @AccountId UNIQUEIDENTIFIER = NULL
)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.SongId, s.SongName, s.SongUrl, s.ImageUrl, s.DurationInSeconds, s.Priority, s.Created,
           s.UploadedByAccountId, ISNULL(v.Value, 0) AS MyVote
    FROM Songs s
    LEFT JOIN SongPriority v ON v.SongId = s.SongId AND v.AccountId = @AccountId
    WHERE s.IsDeleted = 0
    ORDER BY s.Priority DESC, s.SongName ASC;
END
GO
CREATE OR ALTER PROCEDURE procSongSearch
(
    @SearchText NVARCHAR(250),
    @AccountId UNIQUEIDENTIFIER = NULL
)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.SongId, s.SongName, s.SongUrl, s.ImageUrl, s.DurationInSeconds, s.Priority, s.Created,
           s.UploadedByAccountId, ISNULL(v.Value, 0) AS MyVote
    FROM Songs s
    LEFT JOIN SongPriority v ON v.SongId = s.SongId AND v.AccountId = @AccountId
    WHERE s.IsDeleted = 0 AND s.SongName LIKE '%' + @SearchText + '%'
    ORDER BY s.Priority DESC, s.SongName ASC;
END
GO

/* 5. procSongHardDelete — permanent removal (row + links + votes) --- */
CREATE OR ALTER PROCEDURE procSongHardDelete
(
    @SongId UNIQUEIDENTIFIER
)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM Songs WHERE SongId = @SongId)
            THROW 50021, 'Song not found.', 1;

        BEGIN TRANSACTION;
            DELETE FROM PlaylistSongs WHERE SongId = @SongId;   -- remove from every playlist
            DELETE FROM SongPriority  WHERE SongId = @SongId;   -- drop its votes
            DELETE FROM Songs         WHERE SongId = @SongId;   -- remove the song itself
        COMMIT TRANSACTION;

        SELECT CAST(1 AS BIT) AS Success, 'Song permanently deleted.' AS Message;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* 6a. procAccountGetAllWithRole — SuperAdmin user directory --------- */
CREATE OR ALTER PROCEDURE procAccountGetAllWithRole
AS
BEGIN
    SET NOCOUNT ON;
    SELECT A.AccountId, A.Email, A.Role, A.IsActive, A.Created,
           U.UserId, U.UserName, U.FullName
    FROM Account A
    LEFT JOIN Users U ON U.AccountId = A.AccountId
    ORDER BY A.Created DESC;
END
GO

/* 6b. procAccountSetRole — SuperAdmin sets User/Admin (never SuperAdmin) */
CREATE OR ALTER PROCEDURE procAccountSetRole
(
    @AccountId UNIQUEIDENTIFIER,
    @Role NVARCHAR(20)
)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM Account WHERE AccountId = @AccountId)
            THROW 50003, 'Account not found.', 1;
        IF @Role NOT IN ('User', 'Admin')
            THROW 50004, 'Role must be User or Admin.', 1;   -- SuperAdmin is DB-only

        UPDATE Account SET Role = @Role, Updated = GETUTCDATE() WHERE AccountId = @AccountId;

        SELECT AccountId, Email, IsActive, Role, Created, Updated FROM Account WHERE AccountId = @AccountId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* 6c. procAccountCascadeDelete — SuperAdmin removes an account + its data */
CREATE OR ALTER PROCEDURE procAccountCascadeDelete
(
    @AccountId UNIQUEIDENTIFIER
)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM Account WHERE AccountId = @AccountId)
            THROW 50003, 'Account not found.', 1;

        BEGIN TRANSACTION;
            DELETE FROM SongPriority WHERE AccountId = @AccountId;                       -- their votes
            DELETE FROM PlaylistSongs WHERE PlaylistId IN (SELECT PlaylistId FROM Playlists WHERE AccountId = @AccountId);
            DELETE FROM Playlists WHERE AccountId = @AccountId;                           -- their playlists
            DELETE FROM Users WHERE AccountId = @AccountId;                               -- their profile
            DELETE FROM Account WHERE AccountId = @AccountId;                             -- the account
            -- Songs they uploaded stay (global library); their UploadedByAccountId simply dangles.
        COMMIT TRANSACTION;

        SELECT CAST(1 AS BIT) AS Success, 'Account and its data deleted.' AS Message;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
