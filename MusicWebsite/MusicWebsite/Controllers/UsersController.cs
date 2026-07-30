using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Users;
using MusicWebsite.Application.Interfaces.Services;
using MusicWebsite.Extensions;

namespace MusicWebsite.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _users;

    public UsersController(IUserService users) => _users = users;

    /// <summary>Get the profile of the currently authenticated user.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetMe()
    {
        var user = await _users.GetByIdAsync(User.GetUserId());
        return Ok(ApiResponse<UserDto>.Ok(user));
    }

    /// <summary>Update the currently authenticated user's profile.</summary>
    [HttpPut("me")]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateMe([FromBody] UpdateUserRequest request)
    {
        var user = await _users.UpdateProfileAsync(User.GetUserId(), request);
        return Ok(ApiResponse<UserDto>.Ok(user, "Profile updated."));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<UserDto>>>> GetAll()
    {
        var users = await _users.GetAllAsync();
        return Ok(ApiResponse<IEnumerable<UserDto>>.Ok(users));
    }

    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetById(Guid userId)
    {
        var user = await _users.GetByIdAsync(userId);
        return Ok(ApiResponse<UserDto>.Ok(user));
    }
}
