using System.Security.Claims;
using MusicWebsite.Application.Common;

namespace MusicWebsite.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetAccountId(this ClaimsPrincipal user)
        => ParseGuidClaim(user, "accountId");

    public static Guid GetUserId(this ClaimsPrincipal user)
        => ParseGuidClaim(user, "userId");

    /// <summary>The caller's role from the JWT ("role" claim). Defaults to "User" if absent.</summary>
    public static string GetRole(this ClaimsPrincipal user)
    {
        var role = user.FindFirstValue("role");
        return string.IsNullOrWhiteSpace(role) ? "User" : role;
    }

    private static Guid ParseGuidClaim(ClaimsPrincipal user, string claimType)
    {
        var value = user.FindFirstValue(claimType);
        if (Guid.TryParse(value, out var id))
            return id;

        throw new AppException("Invalid or missing authentication token.", 401);
    }
}
