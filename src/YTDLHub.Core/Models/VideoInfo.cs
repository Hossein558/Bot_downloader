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

    /// <summary>
    /// Detailed formats list for YouTube platform.
    /// </summary>
    public IReadOnlyList<YoutubeFormatInfo> YoutubeFormats { get; init; } = [];
}

public sealed class YoutubeFormatInfo
{
    public string FormatId { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public string Codec { get; set; } = string.Empty;
    public long? FileSizeBytes { get; set; }

    public string DisplayName =>
        $"{Resolution} ({Container}, {Codec})" +
        (FileSizeBytes.HasValue ? $" - {FileSizeBytes.Value / (1024.0 * 1024.0):F1} MB" : "");
}
