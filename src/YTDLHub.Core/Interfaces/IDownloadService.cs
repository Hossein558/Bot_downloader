using YTDLHub.Core.Enums;
using YTDLHub.Core.Models;

namespace YTDLHub.Core.Interfaces;

public interface IDownloadService
{
    /// <summary>
    /// Fetches video metadata (title, thumbnail, available qualities) for the given URL.
    /// </summary>
    Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// Queues and starts a download for the given URL at the requested quality.
    /// The returned <see cref="DownloadJob"/> fires <c>ProgressChanged</c> events
    /// as yt-dlp reports progress, and transitions through <see cref="JobStatus"/> states.
    /// </summary>
    Task<DownloadJob> StartDownloadAsync(
        string url,
        VideoQuality quality,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves an existing job by ID (null if not found or already cleaned up).
    /// </summary>
    DownloadJob? GetJob(Guid jobId);

    /// <summary>
    /// Detects the platform from a URL without making a network request.
    /// </summary>
    Platform DetectPlatform(string url);
}
