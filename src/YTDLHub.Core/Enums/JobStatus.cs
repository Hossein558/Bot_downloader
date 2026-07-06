namespace YTDLHub.Core.Enums;

public enum JobStatus
{
    Queued,
    Fetching,
    Downloading,
    Merging,
    Completed,
    Failed,
    Cancelled
}
