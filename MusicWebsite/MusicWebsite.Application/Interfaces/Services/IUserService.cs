using MusicWebsite.Application.DTOs.Users;

namespace MusicWebsite.Application.Interfaces.Services;

public interface IUserService
{
    Task<IEnumerable<UserDto>> GetAllAsync();
    Task<UserDto> GetByIdAsync(Guid userId);
    Task<UserDto> GetByUserNameAsync(string userName);
    Task<UserDto> UpdateProfileAsync(Guid userId, UpdateUserRequest request);
}
