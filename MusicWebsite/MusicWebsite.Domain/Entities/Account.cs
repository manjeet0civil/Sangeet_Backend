namespace MusicWebsite.Domain.Entities;

public class Account
{
    public Guid AccountId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Role { get; set; } = "User";
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}
