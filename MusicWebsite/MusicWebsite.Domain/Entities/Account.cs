namespace MusicWebsite.Domain.Entities;

public class Account
{
    public Guid AccountId { get; set; }
    public string Email { get; set; } = string.Empty;

    /// <summary>Null for accounts created through Google sign-in, which have no password.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Google's <c>sub</c> claim when this account can sign in with Google, otherwise null.
    /// An account may have both this and a password; either one alone is enough to sign in.
    /// </summary>
    public string? GoogleSubject { get; set; }

    public bool IsActive { get; set; }
    public string Role { get; set; } = "User";
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}
