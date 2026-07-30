namespace MusicWebsite.Domain.Entities;

public class User
{
    public Guid UserId { get; set; }
    public Guid AccountId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}
