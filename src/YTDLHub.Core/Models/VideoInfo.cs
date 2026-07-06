using YTDLHub.Core.Enums;

namespace YTDLHub.Core.Models;

public sealed class VideoInfo
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? ThumbnailUrl { get; init; }
    public TimeSpan Duration { get; init; }
    public string Uploader { get; init; } = string.Empty;
    public string OriginalUrl { get; init; } = string.Empty;
    public Platform Platform { get; init; }

    /// <summary>
    /// Available quality presets detected by probing yt-dlp formats.
    /// </summary>
    public IReadOnlyList<VideoQuality> AvailableQualities { get; init; } = [];
}
