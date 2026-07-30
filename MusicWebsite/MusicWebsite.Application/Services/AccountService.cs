using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Accounts;
using MusicWebsite.Application.Interfaces.Persistence;
using MusicWebsite.Application.Interfaces.Services;

namespace MusicWebsite.Application.Services;

public class AccountService : IAccountService
{
    private readonly IAccountRepository _accounts;

    public AccountService(IAccountRepository accounts) => _accounts = accounts;

    public async Task<AccountDto> GetByIdAsync(Guid accountId)
    {
        var account = await _accounts.GetByIdAsync(accountId)
            ?? throw new AppException("Account not found.", 404);

        return new AccountDto
        {
            AccountId = account.AccountId,
            Email = account.Email,
            IsActive = account.IsActive,
            Created = account.Created,
            Updated = account.Updated
        };
    }

    public async Task<AccountDto> UpdateAsync(Guid accountId, UpdateAccountRequest request)
    {
        var account = await _accounts.UpdateAsync(accountId, request.Email, request.IsActive);
        return new AccountDto
        {
            AccountId = account.AccountId,
            Email = account.Email,
            IsActive = account.IsActive,
            Created = account.Created,
            Updated = account.Updated
        };
    }

    public Task<MessageResponse> DeleteAsync(Guid accountId) => _accounts.DeleteAsync(accountId);
}
