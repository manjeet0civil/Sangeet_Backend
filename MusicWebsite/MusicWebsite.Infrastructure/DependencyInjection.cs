using Amazon;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MusicWebsite.Application.Interfaces.Media;
using MusicWebsite.Application.Interfaces.Persistence;
using MusicWebsite.Application.Interfaces.Security;
using MusicWebsite.Application.Interfaces.Storage;
using MusicWebsite.Infrastructure.Media;
using MusicWebsite.Infrastructure.Persistence;
using MusicWebsite.Infrastructure.Persistence.Repositories;
using MusicWebsite.Infrastructure.Security;
using MusicWebsite.Infrastructure.Storage;

namespace MusicWebsite.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // PostgreSQL (Supabase). Accepts either a Npgsql key/value connection string or the
        // postgresql:// URI that Supabase shows in the dashboard.
        var connectionString = configuration.GetSection("ConnectionStrings:MusicDatabase").Value
            ?? throw new InvalidOperationException("Connection string 'MusicDatabase' is not configured.");

        services.AddSingleton<IDbConnectionFactory>(
            new NpgsqlConnectionFactory(PostgresConnectionString.Normalize(connectionString)));

        // Options
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<StorageSettings>(configuration.GetSection(StorageSettings.SectionName));

        // Repositories
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISongRepository, SongRepository>();
        services.AddScoped<IPlaylistRepository, PlaylistRepository>();
        services.AddScoped<IPlaylistSongRepository, PlaylistSongRepository>();

        // Security
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IRoleDefaults, RoleDefaults>();
        services.AddSingleton<IUploadLimits, UploadLimits>();

        // Storage — Backblaze B2 (S3-compatible) when configured, otherwise a no-op stub.
        AddStorage(services, configuration);

        // YouTube audio extraction. "YtDlp" = robust (needs yt-dlp.exe); otherwise the pure-.NET scraper.
        services.Configure<YoutubeSettings>(configuration.GetSection(YoutubeSettings.SectionName));
        var youtube = configuration.GetSection(YoutubeSettings.SectionName).Get<YoutubeSettings>() ?? new YoutubeSettings();
        if (string.Equals(youtube.Provider, "YtDlp", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IYoutubeAudioExtractor, YtDlpAudioExtractor>();
        else
            services.AddSingleton<IYoutubeAudioExtractor, YoutubeExplodeAudioExtractor>();

        return services;
    }

    private static void AddStorage(IServiceCollection services, IConfiguration configuration)
    {
        var storage = configuration.GetSection(StorageSettings.SectionName).Get<StorageSettings>() ?? new StorageSettings();

        if (!string.Equals(storage.Provider, "BackblazeB2", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(storage.B2.KeyId)
            || string.IsNullOrWhiteSpace(storage.B2.ApplicationKey))
        {
            services.AddSingleton<IStorageService, StubStorageService>();
            return;
        }

        services.AddSingleton<IAmazonS3>(_ =>
        {
            var config = new AmazonS3Config
            {
                ServiceURL = storage.B2.ServiceUrl,
                ForcePathStyle = true,                 // B2 works most reliably with path-style addressing
                AuthenticationRegion = storage.B2.Region
            };
            return new AmazonS3Client(storage.B2.KeyId, storage.B2.ApplicationKey, config);
        });

        services.AddSingleton<IStorageService, BackblazeB2StorageService>();
    }
}
