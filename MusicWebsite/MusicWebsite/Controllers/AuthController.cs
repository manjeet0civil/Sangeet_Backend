using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Auth;
using MusicWebsite.Application.Interfaces.Services;

namespace MusicWebsite.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>Register a new account + user profile and return a JWT.</summary>
    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register([FromBody] RegisterRequest request)
    {
        var result = await _auth.RegisterAsync(request);
        return Ok(ApiResponse<AuthResponse>.Ok(result, "Registration successful."));
    }

    /// <summary>Authenticate with email + password and return a JWT.</summary>
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request)
    {
        var result = await _auth.LoginAsync(request);
        return Ok(ApiResponse<AuthResponse>.Ok(result, "Login successful."));
    }

    /// <summary>
    /// Sign in with Google. Takes the ID token from the Google Identity Services button, verifies
    /// it server-side, and returns the same JWT as the other two routes — so everything downstream
    /// is unaware of how the user got here. Creates the account on first sign-in, and links to an
    /// existing account when the verified email already has one.
    /// </summary>
    [HttpPost("google")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Google([FromBody] GoogleSignInRequest request)
    {
        var result = await _auth.GoogleSignInAsync(request);
        return Ok(ApiResponse<AuthResponse>.Ok(result, "Login successful."));
    }

    /// <summary>
    /// Stateless logout. JWTs are self-contained, so the client simply discards the token;
    /// this endpoint exists for symmetry and future refresh-token revocation.
    /// </summary>
    [Authorize]
    [HttpPost("logout")]
    public ActionResult<ApiResponse<object>> Logout()
        => Ok(ApiResponse<object>.Ok(new { }, "Logged out."));
}
