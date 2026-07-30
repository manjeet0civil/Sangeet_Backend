using System.Data;
using Npgsql;

namespace MusicWebsite.Infrastructure.Persistence;

/// <summary>
/// Creates PostgreSQL connections (Supabase). Replaces the old SqlConnectionFactory.
/// </summary>
public class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public NpgsqlConnectionFactory(string connectionString)
    {
        _connectionString = connectionString
            ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
}
