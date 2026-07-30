using MusicWebsite.Application.Interfaces.Security;

namespace MusicWebsite.Infrastructure.Security;

public class BcryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    public bool Verify(string password, string passwordHash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch
        {
            // Malformed/legacy hash -> treat as a failed verification rather than throwing.
            return false;
        }
    }
}
