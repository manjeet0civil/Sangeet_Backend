using Microsoft.Extensions.Configuration;
using MusicWebsite.Application.Common;
using MusicWebsite.Application.Interfaces.Security;

namespace MusicWebsite.Infrastructure.Security;

/// <summary>
/// Reads the default registration role from config key <c>Roles:DefaultRole</c>. Anything other
/// than "User" or "Admin" (including "SuperAdmin") falls back to "Admin" — SuperAdmin is granted
/// only by a direct database update, never through the app.
/// </summary>
public class RoleDefaults : IRoleDefaults
{
    public string DefaultRole { get; }

    public RoleDefaults(IConfiguration configuration)
    {
        var configured = configuration["Roles:DefaultRole"]?.Trim();
        DefaultRole = string.Equals(configured, Roles.User, StringComparison.OrdinalIgnoreCase)
            ? Roles.User
            : Roles.Admin;   // default (per project design) — never SuperAdmin
    }
}
