namespace MusicWebsite.Extensions;

/// <summary>
/// Tiny ".env" reader (no NuGet package needed). Call <see cref="Load"/> BEFORE
/// <c>WebApplication.CreateBuilder</c> so the values land in the environment and therefore in
/// IConfiguration, where they can be read like any other setting.
///
/// Format: KEY=VALUE per line, '#' starts a comment, quotes around the value are optional.
/// A real environment variable always wins over the file, so a server/CI can override without
/// editing the file.
/// </summary>
public static class DotEnv
{
    /// <summary>Loads the nearest .env file. Returns its path, or null if none was found.</summary>
    public static string? Load(string fileName = ".env")
    {
        var path = Find(fileName);
        if (path is null) return null;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            // Don't clobber a real environment variable — that's the deployment's override.
            if (Environment.GetEnvironmentVariable(key) is not null) continue;
            Environment.SetEnvironmentVariable(key, value);
        }

        return path;
    }

    /// <summary>
    /// Looks for the file next to the running app and next to the current directory, walking a few
    /// levels up. That covers both "dotnet run" (project folder) and a published build (exe folder).
    /// </summary>
    private static string? Find(string fileName)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var depth = 0; dir is not null && depth < 5; depth++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, fileName);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
