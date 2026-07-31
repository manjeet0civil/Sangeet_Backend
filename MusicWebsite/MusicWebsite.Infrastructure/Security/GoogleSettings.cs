namespace MusicWebsite.Infrastructure.Security;

/// <summary>Configuration for "Sign in with Google" (<c>Google</c> section).</summary>
public class GoogleSettings
{
    public const string SectionName = "Google";

    /// <summary>
    /// The OAuth 2.0 Web application client id from Google Cloud Console, e.g.
    /// <c>1234-abcd.apps.googleusercontent.com</c>.
    /// <para>
    /// It is not a secret — the browser sends it too — but it IS the audience every ID token is
    /// checked against, so a wrong value here rejects every sign-in. Empty disables the feature:
    /// the endpoint then returns 501 rather than accepting tokens it cannot check.
    /// </para>
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
}
