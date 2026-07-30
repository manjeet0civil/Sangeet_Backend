using System.ComponentModel.DataAnnotations;

namespace MusicWebsite.Application.DTOs.Accounts;

public class AccountDto
{
    public Guid AccountId { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}

public class UpdateAccountRequest
{
    [Required, EmailAddress, MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
