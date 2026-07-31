using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using MusicWebsite.Application.Common;
using MusicWebsite.Application.Interfaces.Security;
using MusicWebsite.Application.Models;

namespace MusicWebsite.Infrastructure.Security;

/// <summary>
/// Validates Google ID tokens with Google's own library.
///
/// <para>
/// <see cref="GoogleJsonWebSignature.ValidateAsync"/> does the work that makes this safe: it
/// fetches and caches Google's public signing keys, checks the signature against them, and
/// rejects a token whose issuer, expiry or audience is wrong. Audience is the important one —
/// without pinning it to our own client id, a token minted for any other Google application
/// would be accepted here, which is the classic way this integration gets broken into.
/// </para>
/// </summary>
public class GoogleTokenValidator : IGoogleTokenValidator
{
    private readonly GoogleSettings _settings;

    public GoogleTokenValidator(IOptions<GoogleSettings> settings) => _settings = settings.Value;

    public async Task<GoogleIdentity> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
            throw new AppException("Google sign-in is not configured on this server.", 501);

        if (string.IsNullOrWhiteSpace(idToken))
            throw new AppException("No Google credential was supplied.", 400);

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _settings.ClientId }
            });
        }
        catch (InvalidJwtException ex)
        {
            // Expired, tampered with, or issued for a different application. The detail goes to the
            // caller only as a generic failure — it isn't useful to them and can be to an attacker.
            throw new AppException("That Google sign-in could not be verified. Please try again.", 401, ex);
        }

        if (string.IsNullOrWhiteSpace(payload.Subject))
            throw new AppException("That Google sign-in could not be verified. Please try again.", 401);

        if (string.IsNullOrWhiteSpace(payload.Email))
            throw new AppException("That Google account has no email address, so it can't be used to sign in.", 400);

        return new GoogleIdentity(
            Subject: payload.Subject,
            Email: payload.Email,
            EmailVerified: payload.EmailVerified,
            Name: payload.Name,
            PictureUrl: payload.Picture);
    }
}
