using Npgsql;

namespace MusicWebsite.Infrastructure.Persistence;

/// <summary>
/// Accepts a PostgreSQL connection string in either form and returns one Npgsql understands.
///
/// Supabase's dashboard shows a URI:
///     postgresql://postgres:PASSWORD@db.abcd.supabase.co:5432/postgres
/// Npgsql only takes key/value form:
///     Host=db.abcd.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=...
///
/// Pasting the dashboard string straight into appsettings.json would otherwise fail; this
/// converts it. SSL is switched on because Supabase refuses unencrypted connections.
/// </summary>
public static class PostgresConnectionString
{
    public static string Normalize(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is empty.", nameof(connectionString));

        var value = connectionString.Trim();

        var builder = value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                   || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            ? FromUri(value)
            : new NpgsqlConnectionStringBuilder(value);

        // Supabase requires TLS. In Npgsql 8, SslMode.Require encrypts without verifying the
        // server certificate chain — right for Supabase, whose chain isn't in the Windows trust
        // store. Use VerifyFull explicitly in the connection string if you pin a root cert.
        if (value.IndexOf("ssl", StringComparison.OrdinalIgnoreCase) < 0)
        {
            builder.SslMode = SslMode.Require;
        }

        return builder.ConnectionString;
    }

    private static NpgsqlConnectionStringBuilder FromUri(string uriString)
    {
        var uri = new Uri(uriString);
        var userInfo = uri.UserInfo.Split(':', 2);
        var database = uri.AbsolutePath.Trim('/');

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = database.Length > 0 ? database : "postgres",
            // The URI form percent-encodes anything special in the username/password.
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
        };

        // Carry over any ?key=value options (e.g. ?sslmode=require).
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2)
                builder[Uri.UnescapeDataString(pair[0])] = Uri.UnescapeDataString(pair[1]);
        }

        return builder;
    }
}
