using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Auth;
using MusicWebsite.Application.DTOs.Users;
using MusicWebsite.Application.Interfaces.Persistence;
using MusicWebsite.Application.Interfaces.Security;
using MusicWebsite.Application.Interfaces.Services;

namespace MusicWebsite.Application.Services;

public class AuthService : IAuthService
{
    private readonly IAccountRepository _accounts;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IRoleDefaults _roleDefaults;

    public AuthService(
        IAccountRepository accounts,
        IUserRepository users,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IRoleDefaults roleDefaults)
    {
        _accounts = accounts;
        _users = users;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _roleDefaults = roleDefaults;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var accountId = Guid.NewGuid();
        var passwordHash = _passwordHasher.Hash(request.Password);

        // procAccountInsert throws 50001 (Email already exists) -> translated to 409.
        var account = await _accounts.InsertAsync(accountId, request.Email, passwordHash, _roleDefaults.DefaultRole);

        MusicWebsite.Domain.Entities.User user;
        try
        {
            var userId = Guid.NewGuid();
            // procUserInsert throws 50011 (Username already exists) -> translated to 409.
            user = await _users.InsertAsync(userId, account.AccountId, request.UserName, request.FullName, request.ProfileImageUrl);
        }
        catch
        {
            // Compensate: the two inserts are separate transactions, so roll back the orphan account.
            await _accounts.DeleteAsync(account.AccountId);
            throw;
        }

        var dto = new UserDto
        {
            UserId = user.UserId,
            AccountId = account.AccountId,
            UserName = user.UserName,
            FullName = user.FullName,
            ProfileImageUrl = user.ProfileImageUrl,
            Email = account.Email,
            Role = account.Role,
            Created = user.Created,
            Updated = user.Updated
        };

        return BuildAuthResponse(dto);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var credentials = await _accounts.GetCredentialsByEmailAsync(request.Email);

        // Same generic message whether the account is missing or the password is wrong.
        if (credentials is null || !_passwordHasher.Verify(request.Password, credentials.PasswordHash))
            throw new AppException("Invalid email or password.", 401);

        var profile = await _users.GetByAccountIdAsync(credentials.AccountId)
            ?? throw new AppException("No user profile is associated with this account.", 404);

        // The profile row also carries the joined role, but trust the login projection as the source.
        profile.Role = credentials.Role;
        return BuildAuthResponse(profile);
    }

    private AuthResponse BuildAuthResponse(UserDto user)
    {
        var token = _tokenService.CreateToken(user.AccountId, user.UserId, user.Email ?? string.Empty, user.UserName, user.Role);
        return new AuthResponse
        {
            AccessToken = token.Token,
            TokenType = "Bearer",
            ExpiresAtUtc = token.ExpiresAtUtc,
            User = user
        };
    }
}
