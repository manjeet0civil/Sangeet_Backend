using MusicWebsite.Application.Models;

namespace MusicWebsite.Application.Interfaces.Security;

public interface ITokenService
{
    TokenResult CreateToken(Guid accountId, Guid userId, string email, string userName, string role);
}
