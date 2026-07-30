using MusicWebsite.Application.DTOs.Users;
using MusicWebsite.Domain.Entities;

namespace MusicWebsite.Application.Interfaces.Persistence;

public interface IUserRepository
{
    Task<User> InsertAsync(Guid userId, Guid accountId, string userName, string fullName, string? profileImageUrl);
    Task<User> UpdateAsync(Guid userId, string userName, string fullName, string? profileImageUrl);
    Task<UserDto?> GetByIdAsync(Guid userId);
    Task<UserDto?> GetByAccountIdAsync(Guid accountId);
    Task<UserDto?> GetByUserNameAsync(string userName);
    Task<IEnumerable<UserDto>> GetAllAsync();
}
