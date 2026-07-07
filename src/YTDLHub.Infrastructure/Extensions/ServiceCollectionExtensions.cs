using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YTDLHub.Core.Interfaces;
using YTDLHub.Infrastructure.Data;
using YTDLHub.Infrastructure.Options;
using YTDLHub.Infrastructure.Services;

namespace YTDLHub.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all YTDLHub.Infrastructure services into the DI container.
    /// Call this from both the API and Bot projects.
    /// </summary>
    public static IServiceCollection AddYtDlpInfrastructure(
        this IServiceCollection services,
        Action<YtDlpOptions>? configure = null)
    {
        services.AddOptions<YtDlpOptions>()
                .BindConfiguration(YtDlpOptions.SectionName)
                .PostConfigure(opts =>
                {
                    if (string.IsNullOrWhiteSpace(opts.ExecutablePath) || opts.ExecutablePath.Contains("yt-dlp.exe"))
                    {
                        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                        {
                            // Resolve local data/yt-dlp.exe during development
                            var dataDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data"));
                            var exePath = Path.Combine(dataDir, "yt-dlp.exe");
                            opts.ExecutablePath = File.Exists(exePath) ? exePath : "yt-dlp";
                            
                            // Also set DownloadDirectory to the shared data folder
                            if (opts.DownloadDirectory == "downloads" || opts.DownloadDirectory.Contains("data\\downloads"))
                            {
                                opts.DownloadDirectory = Path.Combine(dataDir, "downloads");
                            }
                        }
                        else
                        {
                            opts.ExecutablePath = "yt-dlp"; // Assumed globally installed on Linux
                            opts.DownloadDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "downloads"));
                        }
                    }
                });

        if (configure is not null)
            services.PostConfigure(configure);

        // Singleton so the job dictionary persists across requests
        services.AddSingleton<IDownloadService, YtDlpService>();

        return services;
    }

    /// <summary>
    /// Registers the SQLite database and user/auth services.
    /// </summary>
    public static IServiceCollection AddYtDlpDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var dbPath = configuration["Database:Path"] ?? "ytdlhub.db";

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlite($"Data Source={dbPath}"));

        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAuthService, TelegramAuthService>();

        return services;
    }
}
