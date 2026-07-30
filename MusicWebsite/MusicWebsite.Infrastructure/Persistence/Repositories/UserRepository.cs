using MusicWebsite.Application.DTOs.Users;
using MusicWebsite.Application.Interfaces.Persistence;
using MusicWebsite.Domain.Entities;

namespace MusicWebsite.Infrastructure.Persistence.Repositories;

public class UserRepository : RepositoryBase, IUserRepository
{
    public UserRepository(IDbConnectionFactory factory) : base(factory) { }

    public Task<User> InsertAsync(Guid userId, Guid accountId, string userName, string fullName, string? profileImageUrl)
        => QueryFirstAsync<User>(StoredProcedures.UserInsert,
            new { UserId = userId, AccountId = accountId, UserName = userName, FullName = fullName, ProfileImageUrl = profileImageUrl });

    public Task<User> UpdateAsync(Guid userId, string userName, string fullName, string? profileImageUrl)
        => QueryFirstAsync<User>(StoredProcedures.UserUpdate,
            new { UserId = userId, FullName = fullName, ProfileImageUrl = profileImageUrl, UserName = userName });

    public Task<UserDto?> GetByIdAsync(Guid userId)
        => QuerySingleOrDefaultAsync<UserDto>(StoredProcedures.UserGetById, new { UserId = userId });

    public Task<UserDto?> GetByAccountIdAsync(Guid accountId)
        => QuerySingleOrDefaultAsync<UserDto>(StoredProcedures.UserGetByAccountId, new { AccountId = accountId });

    public Task<UserDto?> GetByUserNameAsync(string userName)
        => QuerySingleOrDefaultAsync<UserDto>(StoredProcedures.UserGetByUserName, new { UserName = userName });

    public Task<IEnumerable<UserDto>> GetAllAsync()
        => QueryAsync<UserDto>(StoredProcedures.UserGetAll);
}
