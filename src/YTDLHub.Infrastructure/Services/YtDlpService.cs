using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YTDLHub.Core.Enums;
using YTDLHub.Core.Interfaces;
using YTDLHub.Core.Models;
using YTDLHub.Infrastructure.Options;

namespace YTDLHub.Infrastructure.Services;

/// <summary>
/// Concrete implementation of <see cref="IDownloadService"/> using yt-dlp as a child process.
/// </summary>
public sealed class YtDlpService : IDownloadService
{
    // ── Regex patterns ──────────────────────────────────────────────────────
    private static readonly Regex ProgressRegex = new(
        @"\[download\]\s+(?<pct>[\d.]+)%.*?at\s+(?<speed>[\d.]+\s*\S+).*?ETA\s+(?<eta>[\d:]+)",
        RegexOptions.Compiled);

    private static readonly Regex YoutubeRegex = new(
        @"(youtube\.com|youtu\.be)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex InstagramRegex = new(
        @"instagram\.com",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ── Fields ───────────────────────────────────────────────────────────────
    private readonly YtDlpOptions _opts;
    private readonly ILogger<YtDlpService> _logger;
    private readonly ConcurrentDictionary<Guid, DownloadJob> _jobs = new();
    private readonly SemaphoreSlim _concurrencyGate;

    public YtDlpService(IOptions<YtDlpOptions> opts, ILogger<YtDlpService> logger)
    {
        _opts = opts.Value;
        _logger = logger;
        _concurrencyGate = new SemaphoreSlim(_opts.MaxConcurrentDownloads, _opts.MaxConcurrentDownloads);

        // Ensure the download directory exists
        Directory.CreateDirectory(_opts.DownloadDirectory);
    }

    // ── IDownloadService ─────────────────────────────────────────────────────

    public Platform DetectPlatform(string url)
    {
        if (YoutubeRegex.IsMatch(url))   return Platform.YouTube;
        if (InstagramRegex.IsMatch(url)) return Platform.Instagram;
        return Platform.Unknown;
    }

    public async Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching info for URL: {Url}", url);

        var args = BuildBaseArgs();
        args.AddRange(["--dump-json", "--no-playlist", "--no-warnings", url]);

        var json = await RunProcessAsync(args, ct);
        return ParseVideoInfo(url, json);
    }

    public async Task<DownloadJob> StartDownloadAsync(
        string url,
        VideoQuality quality,
        CancellationToken ct = default)
    {
        var job = new DownloadJob { Url = url, Quality = quality };
        _jobs[job.Id] = job;

        // Fire-and-forget on a thread-pool thread so the caller is not blocked
        _ = Task.Run(() => RunDownloadAsync(job, ct), ct);

        return job;
    }

    public DownloadJob? GetJob(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job : null;

    // ── Internal helpers ─────────────────────────────────────────────────────

    private async Task RunDownloadAsync(DownloadJob job, CancellationToken ct)
    {
        await _concurrencyGate.WaitAsync(ct);
        try
        {
            job.Status = JobStatus.Downloading;
            job.RaiseProgressChanged();

            var outputTemplate = Path.Combine(_opts.DownloadDirectory, $"{job.Id}.%(ext)s");

            var args = BuildBaseArgs();

            // Merge video+audio to mp4; for audio-only, convert to requested codec
            if (job.Quality is VideoQuality.AudioMp3)
            {
                args.AddRange(["-x", "--audio-format", "mp3"]);
            }
            else if (job.Quality is VideoQuality.AudioM4A)
            {
                args.AddRange(["-x", "--audio-format", "m4a"]);
            }
            else
            {
                args.AddRange([
                    "-f", job.Quality.ToFormatSelector(),
                    "--merge-output-format", "mp4"
                ]);
            }

            args.AddRange([
                "--newline",             // one progress line per line (easier to parse)
                "--progress",
                "-o", outputTemplate,
                "--no-playlist",
                job.Url
            ]);

            _logger.LogInformation("Starting download job {JobId} | quality={Quality}", job.Id, job.Quality);

            using var process = CreateProcess(args);
            process.OutputDataReceived += (_, e) => HandleProgressLine(job, e.Data);
            process.ErrorDataReceived  += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _logger.LogWarning("[yt-dlp stderr] {Line}", e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                job.Status       = JobStatus.Failed;
                job.ErrorMessage = "yt-dlp خطا برگرداند. لطفاً لینک را بررسی کنید.";
                job.RaiseProgressChanged();
                return;
            }

            // Locate the output file (extension may differ)
            var outputFile = Directory
                .EnumerateFiles(_opts.DownloadDirectory, $"{job.Id}.*")
                .FirstOrDefault();

            if (outputFile is null)
            {
                job.Status       = JobStatus.Failed;
                job.ErrorMessage = "فایل خروجی پیدا نشد.";
                job.RaiseProgressChanged();
                return;
            }

            var fi = new FileInfo(outputFile);
            job.FilePath       = outputFile;
            job.FileName       = fi.Name;
            job.FileSizeBytes  = fi.Length;
            job.Progress       = 100;
            job.Status         = JobStatus.Completed;
            job.CompletedAt    = DateTime.UtcNow;
            job.RaiseProgressChanged();

            _logger.LogInformation("Job {JobId} completed. File: {File} ({Size} bytes)", job.Id, outputFile, fi.Length);

            // Schedule cleanup
            _ = Task.Delay(TimeSpan.FromMinutes(_opts.FileRetentionMinutes))
                    .ContinueWith(_ => CleanupJob(job));
        }
        catch (OperationCanceledException)
        {
            job.Status       = JobStatus.Cancelled;
            job.ErrorMessage = "دانلود لغو شد.";
            job.RaiseProgressChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download job {JobId} failed", job.Id);
            job.Status       = JobStatus.Failed;
            job.ErrorMessage = "خطای داخلی رخ داد. لطفاً دوباره تلاش کنید.";
            job.RaiseProgressChanged();
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    private void HandleProgressLine(DownloadJob job, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        // Detect merging phase
        if (line.Contains("[Merger]") || line.Contains("Merging"))
        {
            job.Status = JobStatus.Merging;
            job.RaiseProgressChanged();
            return;
        }

        var match = ProgressRegex.Match(line);
        if (!match.Success) return;

        if (double.TryParse(match.Groups["pct"].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var pct))
        {
            job.Progress    = (int)Math.Clamp(pct, 0, 100);
            job.SpeedDisplay = match.Groups["speed"].Value;
            job.EtaDisplay   = match.Groups["eta"].Value;
            job.Status       = JobStatus.Downloading;
            job.RaiseProgressChanged();
        }
    }

    private VideoInfo ParseVideoInfo(string originalUrl, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Determine which quality presets are actually available
        var availableQualities = new List<VideoQuality>();
        var maxHeight = 0;

        if (root.TryGetProperty("formats", out var formatsEl))
        {
            foreach (var fmt in formatsEl.EnumerateArray())
            {
                if (fmt.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number)
                    maxHeight = Math.Max(maxHeight, h.GetInt32());
            }
        }

        // Audio always available
        availableQualities.Add(VideoQuality.AudioMp3);
        availableQualities.Add(VideoQuality.AudioM4A);

        if (maxHeight >= 360) availableQualities.Insert(0, VideoQuality.SD360);
        if (maxHeight >= 480) availableQualities.Insert(0, VideoQuality.SD480);
        if (maxHeight >= 720) availableQualities.Insert(0, VideoQuality.HD720);
        if (maxHeight >= 1080) availableQualities.Insert(0, VideoQuality.HD1080);
        availableQualities.Insert(0, VideoQuality.Best);

        var durationSec = root.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.Number
            ? dur.GetDouble()
            : 0d;

        return new VideoInfo
        {
            Id                 = root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            Title              = root.TryGetProperty("title", out var t) ? t.GetString() ?? "بدون عنوان" : "بدون عنوان",
            ThumbnailUrl       = root.TryGetProperty("thumbnail", out var th) ? th.GetString() : null,
            Duration           = TimeSpan.FromSeconds(durationSec),
            Uploader           = root.TryGetProperty("uploader", out var up) ? up.GetString() ?? "" : "",
            OriginalUrl        = originalUrl,
            Platform           = DetectPlatform(originalUrl),
            AvailableQualities = availableQualities
        };
    }

    private async Task<string> RunProcessAsync(List<string> args, CancellationToken ct)
    {
        using var process = CreateProcess(args);
        var sb     = new StringBuilder();
        var errSb  = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) errSb.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var msg = errSb.ToString().Trim();
            _logger.LogError("yt-dlp failed (exit {Code}): {Err}", process.ExitCode, msg);
            throw new InvalidOperationException(
                "نتوانستیم اطلاعات ویدیو را دریافت کنیم. لطفاً لینک را بررسی کنید.");
        }

        return sb.ToString().Trim();
    }

    private Process CreateProcess(List<string> args)
    {
        // Join args safely – yt-dlp accepts individual args; we pass them via ArgumentList
        var psi = new ProcessStartInfo
        {
            FileName               = _opts.ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        return new Process { StartInfo = psi, EnableRaisingEvents = true };
    }

    private List<string> BuildBaseArgs()
    {
        var args = new List<string>();

        if (!string.IsNullOrWhiteSpace(_opts.ProxyUrl))
        {
            args.Add("--proxy");
            args.Add(_opts.ProxyUrl);
        }

        if (!string.IsNullOrWhiteSpace(_opts.CookiesFilePath) &&
            File.Exists(_opts.CookiesFilePath))
        {
            args.Add("--cookies");
            args.Add(_opts.CookiesFilePath);
        }

        // PO Token sidecar: passes the bgutil provider base URL as an extractor-arg.
        // yt-dlp-get-pot plugin picks this up and requests a PO token before each extraction,
        // which allows bypassing YouTube bot-detection on datacenter IP addresses.
        if (!string.IsNullOrWhiteSpace(_opts.PotProviderUrl))
        {
            args.Add("--extractor-args");
            args.Add($"youtube:getpot_bgutil_baseurl={_opts.PotProviderUrl}");
        }

        return args;
    }

    private void CleanupJob(DownloadJob job)
    {
        if (job.FilePath is not null && File.Exists(job.FilePath))
        {
            try   { File.Delete(job.FilePath); }
            catch { /* best-effort */ }
        }
        _jobs.TryRemove(job.Id, out _);
        _logger.LogInformation("Cleaned up job {JobId}", job.Id);
    }
}
