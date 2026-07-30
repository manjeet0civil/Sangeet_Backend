using System.ComponentModel.DataAnnotations;

namespace MusicWebsite.Application.DTOs.Users;

public class UserDto
{
    public Guid UserId { get; set; }
    public Guid AccountId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "User";
    public DateTime? Created { get; set; }
    public DateTime? Updated { get; set; }
}

public class UpdateUserRequest
{
    [Required, MaxLength(100)]
    public string UserName { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    public string? ProfileImageUrl { get; set; }
}
