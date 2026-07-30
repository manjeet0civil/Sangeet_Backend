using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicWebsite.Application.Common;
using MusicWebsite.Application.DTOs.Accounts;
using MusicWebsite.Application.Interfaces.Services;
using MusicWebsite.Extensions;

namespace MusicWebsite.Controllers;

[ApiController]
[Authorize]
[Route("api/account")]
public class AccountController : ControllerBase
{
    private readonly IAccountService _accounts;

    public AccountController(IAccountService accounts) => _accounts = accounts;

    /// <summary>Get the currently authenticated account (email, status).</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<AccountDto>>> Get()
    {
        var account = await _accounts.GetByIdAsync(User.GetAccountId());
        return Ok(ApiResponse<AccountDto>.Ok(account));
    }

    /// <summary>Update the current account's email / active state.</summary>
    [HttpPut]
    public async Task<ActionResult<ApiResponse<AccountDto>>> Update([FromBody] UpdateAccountRequest request)
    {
        var account = await _accounts.UpdateAsync(User.GetAccountId(), request);
        return Ok(ApiResponse<AccountDto>.Ok(account, "Account updated."));
    }

    /// <summary>Delete the current account.</summary>
    [HttpDelete]
    public async Task<ActionResult<ApiResponse<MessageResponse>>> Delete()
    {
        var result = await _accounts.DeleteAsync(User.GetAccountId());
        return Ok(ApiResponse<MessageResponse>.Ok(result, result.Message));
    }
}
