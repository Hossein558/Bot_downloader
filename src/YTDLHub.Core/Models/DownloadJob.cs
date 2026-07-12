using YTDLHub.Core.Enums;

namespace YTDLHub.Core.Models;

public sealed class DownloadJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Url { get; init; } = string.Empty;
    public VideoQuality Quality { get; init; }
    public string? FormatId { get; set; }
    public string? VideoTitle { get; set; }
    public string? ThumbnailUrl { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public int Progress { get; set; }           // 0-100
    public string? SpeedDisplay { get; set; }
    public string? EtaDisplay { get; set; }
    public string? FilePath { get; set; }
    public string? FileName { get; set; }
    public long FileSizeBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }    // 7 days after completion

    // User & Folder
    public Guid? UserId { get; set; }
    public Guid? FolderId { get; set; }

    // Media Classification
    public Platform Platform { get; set; } = Platform.Unknown;
    public MediaType MediaType { get; set; } = MediaType.Unknown;

    // Telegram context
    public int? TelegramMessageId { get; set; }
    public long? TelegramChatId { get; set; }

    // Caller can subscribe to receive progress updates
    public event Action<DownloadJob>? ProgressChanged;
    public void RaiseProgressChanged() => ProgressChanged?.Invoke(this);
}
