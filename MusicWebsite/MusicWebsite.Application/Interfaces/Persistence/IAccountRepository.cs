using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Admin;
using MusicWebsite.Application.Models;
using MusicWebsite.Domain.Entities;

namespace MusicWebsite.Application.Interfaces.Persistence;

public interface IAccountRepository
{
    Task<Account> InsertAsync(Guid accountId, string email, string passwordHash, string role);
    Task<Account?> GetByIdAsync(Guid accountId);
    Task<AccountCredentials?> GetCredentialsByEmailAsync(string email);
    Task<Account> UpdateAsync(Guid accountId, string email, bool isActive);
    Task<MessageResponse> DeleteAsync(Guid accountId);

    // SuperAdmin operations
    Task<IEnumerable<AdminUserDto>> GetAllWithRoleAsync();
    Task<Account> SetRoleAsync(Guid accountId, string role);
    Task<MessageResponse> CascadeDeleteAsync(Guid accountId);
}
