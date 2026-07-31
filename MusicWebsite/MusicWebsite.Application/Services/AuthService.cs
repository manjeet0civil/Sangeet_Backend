using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Auth;
using MusicWebsite.Application.DTOs.Users;
using MusicWebsite.Application.Interfaces.Persistence;
using MusicWebsite.Application.Interfaces.Security;
using MusicWebsite.Application.Interfaces.Services;
using MusicWebsite.Application.Models;

namespace MusicWebsite.Application.Services;

public class AuthService : IAuthService
{
    private readonly IAccountRepository _accounts;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IRoleDefaults _roleDefaults;
    private readonly IGoogleTokenValidator _googleTokens;

    public AuthService(
        IAccountRepository accounts,
        IUserRepository users,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IRoleDefaults roleDefaults,
        IGoogleTokenValidator googleTokens)
    {
        _accounts = accounts;
        _users = users;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _roleDefaults = roleDefaults;
        _googleTokens = googleTokens;
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
        if (credentials is null)
            throw new AppException("Invalid email or password.", 401);

        // An account created through Google has no password, so there is nothing to verify. Saying
        // so is a deliberate choice: the generic message would leave the owner permanently stuck,
        // guessing at a password that does not exist. It gives away that the account exists, but
        // registration already does that (it returns "Email already exists"), so nothing is lost.
        if (credentials.PasswordHash is null)
            throw new AppException("This account signs in with Google. Use the Google button instead.", 401);

        if (!_passwordHasher.Verify(request.Password, credentials.PasswordHash))
            throw new AppException("Invalid email or password.", 401);

        var profile = await _users.GetByAccountIdAsync(credentials.AccountId)
            ?? throw new AppException("No user profile is associated with this account.", 404);

        // The profile row also carries the joined role, but trust the login projection as the source.
        profile.Role = credentials.Role;
        return BuildAuthResponse(profile);
    }

    /// <summary>
    /// Signs in with a Google ID token, creating the account on first use.
    ///
    /// <para>Three cases, in order:</para>
    /// <list type="number">
    ///   <item>Known Google subject — sign straight in.</item>
    ///   <item>Unknown subject but the verified email matches an existing account — link the two,
    ///         so someone who registered with a password can now use either route. This is only
    ///         safe because Google asserts the address is verified; without that, anyone could
    ///         claim someone else's account by signing up to Google with their address.</item>
    ///   <item>Neither — create a fresh account with no password.</item>
    /// </list>
    /// </summary>
    public async Task<AuthResponse> GoogleSignInAsync(GoogleSignInRequest request)
    {
        var identity = await _googleTokens.ValidateAsync(request.IdToken);

        if (!identity.EmailVerified)
            throw new AppException("This Google account's email address isn't verified, so it can't be used to sign in.", 401);

        // 1. Returning Google user. The lookup only matches active accounts, same as password login.
        var bySubject = await _accounts.GetCredentialsByGoogleSubjectAsync(identity.Subject);
        if (bySubject is not null)
            return await BuildResponseForAccountAsync(bySubject.AccountId, bySubject.Role);

        // 2. Existing account with the same address — attach this Google identity to it.
        var byEmail = await _accounts.GetCredentialsByEmailAsync(identity.Email);
        if (byEmail is not null)
        {
            await _accounts.LinkGoogleAsync(byEmail.AccountId, identity.Subject);
            return await BuildResponseForAccountAsync(byEmail.AccountId, byEmail.Role);
        }

        // 3. Brand new. Mirrors RegisterAsync, including the compensating delete: the account and
        // profile inserts are separate transactions, so a failed profile would strand the account.
        var accountId = Guid.NewGuid();
        var account = await _accounts.InsertGoogleAsync(accountId, identity.Email, identity.Subject, _roleDefaults.DefaultRole);

        Domain.Entities.User user;
        try
        {
            user = await InsertProfileForGoogleUserAsync(account.AccountId, identity);
        }
        catch
        {
            await _accounts.DeleteAsync(account.AccountId);
            throw;
        }

        return BuildAuthResponse(new UserDto
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
        });
    }

    /// <summary>
    /// Creates the profile row for a new Google user, inventing a username that is free.
    /// <para>
    /// Google gives us no username, so one is derived from the email — but usernames are unique and
    /// two people called <c>john@…</c> and <c>john@…</c> at different domains would collide. Each
    /// attempt is checked first and the insert is still guarded, because between the check and the
    /// insert someone else can take the name.
    /// </para>
    /// </summary>
    private async Task<Domain.Entities.User> InsertProfileForGoogleUserAsync(Guid accountId, GoogleIdentity identity)
    {
        var baseName = BuildUserNameSeed(identity.Email);
        var fullName = string.IsNullOrWhiteSpace(identity.Name) ? baseName : identity.Name!;

        for (var attempt = 0; attempt < 6; attempt++)
        {
            // First attempt uses the bare seed; later ones add a short suffix.
            var candidate = attempt == 0 ? baseName : $"{baseName}{Random.Shared.Next(100, 10000)}";

            if (await _users.GetByUserNameAsync(candidate) is not null)
                continue;

            try
            {
                return await _users.InsertAsync(Guid.NewGuid(), accountId, candidate, fullName, identity.PictureUrl);
            }
            catch (AppException ex) when (ex.StatusCode == 409)
            {
                // Lost the race — try another name.
            }
        }

        throw new AppException("Could not allocate a username for this Google account. Please try again.", 409);
    }

    /// <summary>Turns an email address into a plausible username: local part, letters and digits only.</summary>
    private static string BuildUserNameSeed(string email)
    {
        var local = email.Split('@')[0];
        var cleaned = new string(local.Where(char.IsLetterOrDigit).ToArray());

        // Usernames are capped at 100 chars by the DTOs; leave room for the numeric suffix.
        if (cleaned.Length > 20) cleaned = cleaned[..20];

        // A local part of only punctuation would leave nothing at all.
        return cleaned.Length == 0 ? $"user{Random.Shared.Next(1000, 100000)}" : cleaned;
    }

    /// <summary>Loads the profile for an account that has already been authenticated and issues its token.</summary>
    private async Task<AuthResponse> BuildResponseForAccountAsync(Guid accountId, string role)
    {
        var profile = await _users.GetByAccountIdAsync(accountId)
            ?? throw new AppException("No user profile is associated with this account.", 404);

        profile.Role = role;
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
