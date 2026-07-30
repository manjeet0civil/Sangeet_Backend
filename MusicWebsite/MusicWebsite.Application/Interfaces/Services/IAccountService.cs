using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Accounts;

namespace MusicWebsite.Application.Interfaces.Services;

public interface IAccountService
{
    Task<AccountDto> GetByIdAsync(Guid accountId);
    Task<AccountDto> UpdateAsync(Guid accountId, UpdateAccountRequest request);
    Task<MessageResponse> DeleteAsync(Guid accountId);
}
