namespace MusicWebsite.Application.Models;

/// <summary>Credentials projection returned by procAccountLogin (includes the hash for verification).</summary>
public record AccountCredentials(Guid AccountId, string Email, string PasswordHash, bool IsActive, string Role);

/// <summary>Result of issuing a JWT access token.</summary>
public record TokenResult(string Token, DateTime ExpiresAtUtc);

/// <summary>Result of persisting a file to storage (used once upload is wired up).</summary>
public record StorageResult(string Url, string StorageKey);
