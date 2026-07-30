using System.ComponentModel.DataAnnotations;
using MusicWebsite.Application.Common;

namespace MusicWebsite.Application.DTOs.Admin;

/// <summary>A row in the SuperAdmin user directory.</summary>
public class AdminUserDto
{
    public Guid AccountId { get; set; }
    public Guid? UserId { get; set; }
    public string? Email { get; set; }
    public string? UserName { get; set; }
    public string? FullName { get; set; }
    public string Role { get; set; } = "User";
    public bool IsActive { get; set; }
    public DateTime? Created { get; set; }
}

/// <summary>Sets an account's role. Only User/Admin are accepted (SuperAdmin is DB-only).</summary>
public class SetRoleRequest
{
    [Required]
    [RegularExpression($"^({Roles.User}|{Roles.Admin})$", ErrorMessage = "Role must be User or Admin.")]
    public string Role { get; set; } = Roles.User;
}
