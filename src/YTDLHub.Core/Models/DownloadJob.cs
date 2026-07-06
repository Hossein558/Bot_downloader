using YTDLHub.Core.Enums;

namespace YTDLHub.Core.Models;

public sealed class DownloadJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Url { get; init; } = string.Empty;
    public VideoQuality Quality { get; init; }
    public string? VideoTitle { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public int Progress { get; set; }           // 0–100
    public string? SpeedDisplay { get; set; }   // e.g. "3.14MiB/s"
    public string? EtaDisplay { get; set; }     // e.g. "00:42"
    public string? FilePath { get; set; }
    public string? FileName { get; set; }
    public long FileSizeBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // Caller can subscribe to receive progress updates
    public event Action<DownloadJob>? ProgressChanged;
    public void RaiseProgressChanged() => ProgressChanged?.Invoke(this);
}
