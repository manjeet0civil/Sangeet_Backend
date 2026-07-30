using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Users;
using MusicWebsite.Application.Interfaces.Persistence;
using MusicWebsite.Application.Interfaces.Services;

namespace MusicWebsite.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _users;

    public UserService(IUserRepository users) => _users = users;

    public Task<IEnumerable<UserDto>> GetAllAsync() => _users.GetAllAsync();

    public async Task<UserDto> GetByIdAsync(Guid userId)
        => await _users.GetByIdAsync(userId)
           ?? throw new AppException("User not found.", 404);

    public async Task<UserDto> GetByUserNameAsync(string userName)
        => await _users.GetByUserNameAsync(userName)
           ?? throw new AppException("User not found.", 404);

    public async Task<UserDto> UpdateProfileAsync(Guid userId, UpdateUserRequest request)
    {
        // procUserUpdate throws 50012 (not found) / 50013 (username taken).
        await _users.UpdateAsync(userId, request.UserName, request.FullName, request.ProfileImageUrl);

        // Re-fetch so the response includes the joined Email column.
        return await _users.GetByIdAsync(userId)
               ?? throw new AppException("User not found.", 404);
    }
}
