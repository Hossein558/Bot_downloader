namespace YTDLHub.Infrastructure.Options;

public sealed class YtDlpOptions
{
    public const string SectionName = "YtDlp";

    /// <summary>
    /// Path to the yt-dlp executable. Defaults to "yt-dlp" (searched in PATH).
    /// On Windows you can set this to full path e.g. "C:\\Tools\\yt-dlp.exe"
    /// </summary>
    public string ExecutablePath { get; set; } = "yt-dlp";

    /// <summary>
    /// Directory where downloaded files are stored temporarily.
    /// </summary>
    public string DownloadDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "ytdlhub");

    /// <summary>
    /// How long (in minutes) completed files are kept before auto-delete.
    /// </summary>
    public int FileRetentionMinutes { get; set; } = 60;

    /// <summary>
    /// Optional proxy URL passed to yt-dlp via --proxy flag.
    /// Leave empty to skip.
    /// </summary>
    public string? ProxyUrl { get; set; }

    /// <summary>
    /// Optional path to a Netscape cookies file for authenticated downloads (Instagram).
    /// </summary>
    public string? CookiesFilePath { get; set; }

    /// <summary>
    /// Maximum concurrent downloads allowed system-wide.
    /// </summary>
    public int MaxConcurrentDownloads { get; set; } = 3;
}
