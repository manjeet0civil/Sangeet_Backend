using Microsoft.Extensions.DependencyInjection;
using MusicWebsite.Application.Interfaces.Services;
using MusicWebsite.Application.Services;

namespace MusicWebsite.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ISongService, SongService>();
        services.AddScoped<IPlaylistService, PlaylistService>();
        services.AddScoped<IAdminService, AdminService>();
        return services;
    }
}
