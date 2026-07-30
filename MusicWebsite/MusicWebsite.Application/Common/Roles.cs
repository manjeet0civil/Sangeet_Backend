namespace MusicWebsite.Application.Common;

/// <summary>The role names used for authorization. Stored on Account.Role and carried in the JWT.</summary>
public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>Roles that may be assigned through the API. SuperAdmin is deliberately excluded.</summary>
    public static readonly string[] Assignable = { User, Admin };

    public static bool IsValid(string? role) => role is User or Admin or SuperAdmin;
}
