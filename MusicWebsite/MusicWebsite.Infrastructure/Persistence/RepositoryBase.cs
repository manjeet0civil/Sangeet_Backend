using System.Collections.Concurrent;
using Dapper;
using Npgsql;

namespace MusicWebsite.Infrastructure.Persistence;

/// <summary>
/// Shared Dapper plumbing for the repositories. Every call opens a fresh connection, executes a
/// PL/pgSQL function, and routes business errors through <see cref="PostgresErrorTranslator"/>.
///
/// On SQL Server these were stored procedures called with CommandType.StoredProcedure. PostgreSQL
/// functions are called as a SELECT instead, using named-argument notation:
///
///     SELECT * FROM procaccountinsert(p_accountid =&gt; @AccountId, p_email =&gt; @Email)
///
/// The argument list is generated from the anonymous parameter object, so every function parameter
/// must be named <c>p_</c> + the lower-cased C# property name (see db/postgres/02_functions.sql).
/// Named notation means argument ORDER does not matter — only the names.
///
/// (CommandType.StoredProcedure is deliberately not used: Npgsql translates it to CALL, which only
/// works for PostgreSQL PROCEDUREs, not the FUNCTIONs used here.)
/// </summary>
public abstract class RepositoryBase
{
    private readonly IDbConnectionFactory _factory;

    /// <summary>Cache of generated SQL, keyed by function name + parameter type.</summary>
    private static readonly ConcurrentDictionary<(string Function, Type? ParamType), string> SqlCache = new();

    protected RepositoryBase(IDbConnectionFactory factory) => _factory = factory;

    protected async Task<T?> QuerySingleOrDefaultAsync<T>(string function, object? param = null)
    {
        using var db = _factory.CreateConnection();
        try
        {
            return await db.QueryFirstOrDefaultAsync<T>(BuildCall(function, param), param);
        }
        catch (PostgresException ex)
        {
            throw PostgresErrorTranslator.Translate(ex);
        }
    }

    protected async Task<T> QueryFirstAsync<T>(string function, object? param = null)
    {
        using var db = _factory.CreateConnection();
        try
        {
            return await db.QueryFirstAsync<T>(BuildCall(function, param), param);
        }
        catch (PostgresException ex)
        {
            throw PostgresErrorTranslator.Translate(ex);
        }
    }

    protected async Task<IEnumerable<T>> QueryAsync<T>(string function, object? param = null)
    {
        using var db = _factory.CreateConnection();
        try
        {
            return await db.QueryAsync<T>(BuildCall(function, param), param);
        }
        catch (PostgresException ex)
        {
            throw PostgresErrorTranslator.Translate(ex);
        }
    }

    /// <summary>
    /// Runs a paginated query: a page of rows, then a single total-count row.
    /// SQL Server returned both from one procedure; PostgreSQL functions return a single result
    /// set, so the count comes from a companion function named &lt;function&gt;_total that takes the
    /// same arguments. Both statements go in one command, so it is still a single round trip.
    /// </summary>
    protected async Task<(IEnumerable<T> Items, int Total)> QueryPageAsync<T>(string function, object? param = null)
    {
        var sql = $"{BuildCall(function, param)}; {BuildCall(function + "_total", param)};";

        using var db = _factory.CreateConnection();
        try
        {
            using var multi = await db.QueryMultipleAsync(sql, param);
            var items = (await multi.ReadAsync<T>()).ToList();
            var total = await multi.ReadFirstAsync<int>();
            return (items, total);
        }
        catch (PostgresException ex)
        {
            throw PostgresErrorTranslator.Translate(ex);
        }
    }

    /// <summary>
    /// Builds "SELECT * FROM func(p_x =&gt; @X, ...)" from the properties of the parameter object.
    /// </summary>
    private static string BuildCall(string function, object? param)
        => SqlCache.GetOrAdd((function, param?.GetType()), key =>
        {
            var properties = key.ParamType?.GetProperties() ?? Array.Empty<System.Reflection.PropertyInfo>();

            var arguments = string.Join(", ",
                properties.Select(p => $"p_{p.Name.ToLowerInvariant()} => @{p.Name}"));

            return $"SELECT * FROM {key.Function}({arguments})";
        });
}
