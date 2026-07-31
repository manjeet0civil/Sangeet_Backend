using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Admin;
using MusicWebsite.Application.Interfaces.Persistence;
using MusicWebsite.Application.Models;
using MusicWebsite.Domain.Entities;

namespace MusicWebsite.Infrastructure.Persistence.Repositories;

public class AccountRepository : RepositoryBase, IAccountRepository
{
    public AccountRepository(IDbConnectionFactory factory) : base(factory) { }

    public Task<Account> InsertAsync(Guid accountId, string email, string passwordHash, string role)
        => QueryFirstAsync<Account>(StoredProcedures.AccountInsert,
            new { AccountId = accountId, Email = email, PasswordHash = passwordHash, Role = role });

    public Task<Account?> GetByIdAsync(Guid accountId)
        => QuerySingleOrDefaultAsync<Account>(StoredProcedures.AccountGetById,
            new { AccountId = accountId });

    public Task<AccountCredentials?> GetCredentialsByEmailAsync(string email)
        => QuerySingleOrDefaultAsync<AccountCredentials>(StoredProcedures.AccountLogin,
            new { Email = email });

    public Task<AccountCredentials?> GetCredentialsByGoogleSubjectAsync(string googleSubject)
        => QuerySingleOrDefaultAsync<AccountCredentials>(StoredProcedures.AccountGetByGoogle,
            new { GoogleSubject = googleSubject });

    public Task<Account> InsertGoogleAsync(Guid accountId, string email, string googleSubject, string role)
        => QueryFirstAsync<Account>(StoredProcedures.AccountInsertGoogle,
            new { AccountId = accountId, Email = email, GoogleSubject = googleSubject, Role = role });

    public Task<Account> LinkGoogleAsync(Guid accountId, string googleSubject)
        => QueryFirstAsync<Account>(StoredProcedures.AccountLinkGoogle,
            new { AccountId = accountId, GoogleSubject = googleSubject });

    public Task<Account> UpdateAsync(Guid accountId, string email, bool isActive)
        => QueryFirstAsync<Account>(StoredProcedures.AccountUpdate,
            new { AccountId = accountId, Email = email, IsActive = isActive });

    public Task<MessageResponse> DeleteAsync(Guid accountId)
        => QueryFirstAsync<MessageResponse>(StoredProcedures.AccountDelete,
            new { AccountId = accountId });

    public Task<IEnumerable<AdminUserDto>> GetAllWithRoleAsync()
        => QueryAsync<AdminUserDto>(StoredProcedures.AccountGetAllWithRole);

    public Task<Account> SetRoleAsync(Guid accountId, string role)
        => QueryFirstAsync<Account>(StoredProcedures.AccountSetRole,
            new { AccountId = accountId, Role = role });

    public Task<MessageResponse> CascadeDeleteAsync(Guid accountId)
        => QueryFirstAsync<MessageResponse>(StoredProcedures.AccountCascadeDelete,
            new { AccountId = accountId });
}
