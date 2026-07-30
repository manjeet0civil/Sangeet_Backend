namespace MusicWebsite.Application.Interfaces.Security;

/// <summary>Exposes the role assigned to newly-registered accounts (from configuration).</summary>
public interface IRoleDefaults
{
    /// <summary>Role granted on registration — "User" or "Admin" (never "SuperAdmin").</summary>
    string DefaultRole { get; }
}
