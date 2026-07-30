using MusicWebsite.Application.Common;
using Npgsql;

namespace MusicWebsite.Infrastructure.Persistence;

/// <summary>
/// Translates the custom business error codes raised by the PL/pgSQL functions into
/// <see cref="AppException"/> instances carrying the right HTTP status.
///
/// The SQL Server version threw `THROW 500xx, 'message', 1` and read SqlError.Number.
/// PostgreSQL has no such thing, so each function raises the very same number as a custom
/// SQLSTATE — `RAISE EXCEPTION '...' USING ERRCODE = '50001'` — and we parse it back here.
/// The code→status table below is unchanged, so the API returns exactly what it did before.
/// Any other PostgresException is returned unchanged so it surfaces as a 500.
/// </summary>
public static class PostgresErrorTranslator
{
    // 409 Conflict — uniqueness violations
    private static readonly HashSet<int> Conflict = new()
    {
        50001, // Email already exists
        50011, // Username already exists (insert)
        50013, // Username already exists (update)
        50032, // Playlist already exists
        50035, // Playlist name already exists
        50042, // Song already exists in playlist
    };

    // 404 Not Found — missing rows
    private static readonly HashSet<int> NotFound = new()
    {
        50002, 50003,               // Account not found
        50012,                      // User not found
        50020, 50021,               // Song not found
        50033, 50036, 50037,        // Playlist does not exist
        50038,                      // Account does not exist (playlist lookup)
        50040, 50041,               // Playlist / Song does not exist (add)
        50043,                      // Song not in playlist
        50044,                      // Playlist does not exist (songs)
    };

    // 400 Bad Request — validation / referential
    private static readonly HashSet<int> BadRequest = new()
    {
        50010,          // Account does not exist (user insert)
        50030,          // Account does not exist (playlist insert)
        50031, 50034,   // Playlist name is required
    };

    public static Exception Translate(PostgresException ex)
    {
        if (int.TryParse(ex.SqlState, out var code) && code >= 50000)
        {
            var status = Conflict.Contains(code) ? 409
                       : NotFound.Contains(code) ? 404
                       : BadRequest.Contains(code) ? 400
                       : 400;

            // MessageText is the text passed to RAISE EXCEPTION, without PostgreSQL's decoration.
            return new AppException(ex.MessageText, status);
        }

        // Not a business rule error — let it bubble up as a 500.
        return ex;
    }
}
