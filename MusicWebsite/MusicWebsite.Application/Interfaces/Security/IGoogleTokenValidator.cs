using MusicWebsite.Application.Models;

namespace MusicWebsite.Application.Interfaces.Security;

/// <summary>
/// Verifies the ID token the browser gets back from Google and reports who it belongs to.
/// </summary>
public interface IGoogleTokenValidator
{
    /// <summary>
    /// Checks the token's signature, issuer, expiry and audience, and returns the identity it asserts.
    /// <para>
    /// This is the security boundary of Google sign-in: the token arrives from the browser, so
    /// nothing in it may be trusted until it has been verified against Google's published keys.
    /// Throws when the token is invalid, expired, or issued for a different application.
    /// </para>
    /// </summary>
    Task<GoogleIdentity> ValidateAsync(string idToken, CancellationToken cancellationToken = default);
}
