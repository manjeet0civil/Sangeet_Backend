using MusicWebsite.Application.DTOs.Auth;

namespace MusicWebsite.Application.Interfaces.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);

    /// <summary>Signs in with a verified Google ID token, creating or linking the account as needed.</summary>
    Task<AuthResponse> GoogleSignInAsync(GoogleSignInRequest request);
}
