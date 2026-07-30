using System.Text;

namespace MusicWebsite.Extensions;

/// <summary>
/// Verifies the settings the app cannot start without, and fails with a message that names every
/// missing one plus the environment variable that supplies it.
///
/// Why this exists: <c>appsettings.json</c> is gitignored, so an image built from the repository has
/// no secrets in it at all — they must come from environment variables. Without this check the app
/// crashes on whichever setting it touches first ("Jwt settings are not configured"), which on a
/// platform like Render looks like an endless crash-loop with no hint about the other four values
/// that are also missing.
/// </summary>
public static class StartupConfigCheck
{
    /// <summary>Config key → the environment variable that sets it (":" becomes "__").</summary>
    private static readonly (string Key, string Description)[] Required =
    {
        ("ConnectionStrings:MusicDatabase", "PostgreSQL/Supabase connection string"),
        ("Jwt:Key",                         "JWT signing secret (32+ characters)"),
        ("Jwt:Issuer",                      "JWT issuer"),
        ("Jwt:Audience",                    "JWT audience"),
    };

    public static void Validate(IConfiguration configuration)
    {
        var problems = new List<string>();

        foreach (var (key, description) in Required)
        {
            if (string.IsNullOrWhiteSpace(configuration[key]))
                problems.Add($"  {key.Replace(":", "__"),-40} — missing: {description}");
        }

        // HMAC-SHA256 signing needs at least 256 bits of key, or the token handler throws a much
        // less obvious exception the first time somebody tries to log in.
        var jwtKey = configuration["Jwt:Key"];
        if (!string.IsNullOrWhiteSpace(jwtKey) && Encoding.UTF8.GetByteCount(jwtKey) < 32)
            problems.Add($"  {"Jwt__Key",-40} — too short ({Encoding.UTF8.GetByteCount(jwtKey)} bytes); needs 32+");

        if (problems.Count == 0) return;

        var message = new StringBuilder()
            .AppendLine("The application cannot start — required configuration is missing.")
            .AppendLine()
            .AppendLine("Set these as environment variables (double underscore = nested section):")
            .AppendLine(string.Join(Environment.NewLine, problems))
            .AppendLine()
            .AppendLine("Storage__B2__KeyId / Storage__B2__ApplicationKey are optional: without them")
            .AppendLine("the app still starts and uses a no-op storage stub, but uploads will not work.")
            .ToString();

        throw new InvalidOperationException(message);
    }
}
