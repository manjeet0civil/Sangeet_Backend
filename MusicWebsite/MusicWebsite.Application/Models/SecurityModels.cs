namespace MusicWebsite.Application.Models;

/// <summary>
/// Credentials projection returned by procAccountLogin (includes the hash for verification).
/// <para>
/// <c>PasswordHash</c> is null for accounts created through Google sign-in, which have no password
/// at all — so every password check has to rule that out before verifying.
/// </para>
/// </summary>
public record AccountCredentials(Guid AccountId, string Email, string? PasswordHash, bool IsActive, string Role);

/// <summary>
/// The verified contents of a Google ID token: who Google says signed in.
/// <para>
/// <c>Subject</c> is Google's <c>sub</c> claim — the stable id for the Google account, which is what
/// we store. The email is deliberately not the identifier, because Google lets people change it.
/// </para>
/// </summary>
public record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? Name, string? PictureUrl);

/// <summary>Result of issuing a JWT access token.</summary>
public record TokenResult(string Token, DateTime ExpiresAtUtc);

/// <summary>Result of persisting a file to storage (used once upload is wired up).</summary>
public record StorageResult(string Url, string StorageKey);
