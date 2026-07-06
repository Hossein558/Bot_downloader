using Microsoft.Extensions.DependencyInjection;
using YTDLHub.Core.Interfaces;
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
                .BindConfiguration(YtDlpOptions.SectionName);

        if (configure is not null)
            services.PostConfigure(configure);

        // Singleton so the job dictionary persists across requests
        services.AddSingleton<IDownloadService, YtDlpService>();

        return services;
    }
}
