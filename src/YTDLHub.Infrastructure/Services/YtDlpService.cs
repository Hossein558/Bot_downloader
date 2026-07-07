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

        var platform = DetectPlatform(url);
        if (platform == Platform.Instagram)
        {
            return await GetInstagramInfoAsync(url, ct);
        }

        var args = BuildBaseArgs(url);
        args.AddRange(["--dump-json", "--no-playlist", "--no-warnings", url]);

        var json = await RunProcessAsync(_opts.ExecutablePath, args, ct);
        return ParseVideoInfo(url, json);
    }

    private async Task<VideoInfo> GetInstagramInfoAsync(string url, CancellationToken ct)
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(_opts.CookiesFilePath) && File.Exists(_opts.CookiesFilePath))
        {
            args.Add("--cookies");
            args.Add(_opts.CookiesFilePath);
        }
        args.AddRange(["-j", url]);

        var json = await RunProcessAsync("gallery-dl", args, ct);
        return ParseGalleryDlInfo(url, json);
    }

    public async Task<DownloadJob> StartDownloadAsync(
        string url,
        VideoQuality quality,
        string? formatId = null,
        CancellationToken ct = default)
    {
        var job = new DownloadJob { Url = url, Quality = quality, FormatId = formatId };
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

            var platform = DetectPlatform(job.Url);
            string? outputFile = null;

            if (platform == Platform.Instagram)
            {
                outputFile = await DownloadInstagramAsync(job, ct);
            }
            else
            {
                var outputTemplate = Path.Combine(_opts.DownloadDirectory, $"{job.Id}.%(ext)s");
                var args = BuildBaseArgs(job.Url);

                // Merge video+audio to mp4; for audio-only, convert to requested codec
                if (!string.IsNullOrEmpty(job.FormatId))
                {
                    args.AddRange([
                        "-f", job.FormatId,
                        "--merge-output-format", "mp4"
                    ]);
                }
                else if (job.Quality is VideoQuality.AudioMp3)
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

                using var process = CreateProcess(_opts.ExecutablePath, args);
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

                outputFile = Directory
                    .EnumerateFiles(_opts.DownloadDirectory, $"{job.Id}.*")
                    .FirstOrDefault();
            }

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
        var youtubeFormats = new List<YoutubeFormatInfo>();

        if (root.TryGetProperty("formats", out var formatsEl) && formatsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var fmt in formatsEl.EnumerateArray())
            {
                if (fmt.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number)
                    maxHeight = Math.Max(maxHeight, h.GetInt32());
            }

            var platform = DetectPlatform(originalUrl);
            if (platform == Platform.YouTube)
            {
                // Find best audio formats for merging
                string bestM4aAudioId = "140";
                string bestWebmAudioId = "251";
                long maxM4aAudioSize = 0;
                long maxWebmAudioSize = 0;

                foreach (var fmt in formatsEl.EnumerateArray())
                {
                    var vcodec = fmt.TryGetProperty("vcodec", out var vc) ? vc.GetString() : null;
                    var acodec = fmt.TryGetProperty("acodec", out var ac) ? ac.GetString() : null;
                    var ext = fmt.TryGetProperty("ext", out var e) ? e.GetString() : null;
                    var fid = fmt.TryGetProperty("format_id", out var f) ? f.GetString() : null;

                    if (vcodec == "none" && acodec != "none" && !string.IsNullOrEmpty(fid))
                    {
                        long size = fmt.TryGetProperty("filesize", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
                        if (size == 0 && fmt.TryGetProperty("filesize_approx", out var sa) && sa.ValueKind == JsonValueKind.Number)
                            size = sa.GetInt64();

                        if (ext == "m4a" && size >= maxM4aAudioSize)
                        {
                            maxM4aAudioSize = size;
                            bestM4aAudioId = fid;
                        }
                        else if (ext == "webm" && size >= maxWebmAudioSize)
                        {
                            maxWebmAudioSize = size;
                            bestWebmAudioId = fid;
                        }
                    }
                }

                // Process video formats
                var uniqueFormats = new Dictionary<string, YoutubeFormatInfo>();

                foreach (var fmt in formatsEl.EnumerateArray())
                {
                    var vcodec = fmt.TryGetProperty("vcodec", out var vc) ? vc.GetString() : null;
                    var acodec = fmt.TryGetProperty("acodec", out var ac) ? ac.GetString() : null;
                    var ext = fmt.TryGetProperty("ext", out var e) ? e.GetString() : null;
                    var fid = fmt.TryGetProperty("format_id", out var f) ? f.GetString() : null;

                    if (vcodec != "none" && vcodec != null && !string.IsNullOrEmpty(fid))
                    {
                        int height = fmt.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt32() : 0;
                        if (height < 360) continue; // skip very low resolutions

                        long size = fmt.TryGetProperty("filesize", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
                        if (size == 0 && fmt.TryGetProperty("filesize_approx", out var sa) && sa.ValueKind == JsonValueKind.Number)
                            size = sa.GetInt64();

                        string codecName = "unknown";
                        if (vcodec.Contains("avc1") || vcodec.Contains("h264")) codecName = "h264";
                        else if (vcodec.Contains("vp09") || vcodec.Contains("vp9")) codecName = "vp9";
                        else if (vcodec.Contains("av01") || vcodec.Contains("av1")) codecName = "av1";

                        string container = ext ?? "mp4";
                        string formatKey = $"{height}_{container}";

                        string finalFormatId = fid;
                        long finalSize = size;

                        if (acodec == "none")
                        {
                            if (container == "webm")
                            {
                                finalFormatId = $"{fid}+{bestWebmAudioId}";
                                finalSize += maxWebmAudioSize;
                            }
                            else
                            {
                                finalFormatId = $"{fid}+{bestM4aAudioId}";
                                finalSize += maxM4aAudioSize;
                            }
                        }

                        var formatInfo = new YoutubeFormatInfo
                        {
                            FormatId = finalFormatId,
                            Resolution = $"{height}p",
                            Container = container.ToUpper(),
                            Codec = codecName,
                            FileSizeBytes = finalSize > 0 ? finalSize : null
                        };

                        uniqueFormats[formatKey] = formatInfo;
                    }
                }

                // Add standard Audio presets too
                youtubeFormats.AddRange(uniqueFormats.Values.OrderByDescending(f => int.Parse(f.Resolution.Replace("p", ""))));
                youtubeFormats.Add(new YoutubeFormatInfo { FormatId = bestM4aAudioId, Resolution = "Audio", Container = "M4A", Codec = "aac", FileSizeBytes = maxM4aAudioSize > 0 ? maxM4aAudioSize : null });
                youtubeFormats.Add(new YoutubeFormatInfo { FormatId = "mp3", Resolution = "Audio", Container = "MP3", Codec = "mp3", FileSizeBytes = maxM4aAudioSize > 0 ? maxM4aAudioSize : null }); // approximate size
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
            AvailableQualities = availableQualities,
            YoutubeFormats     = youtubeFormats
        };
    }

    private async Task<string> RunProcessAsync(string executable, List<string> args, CancellationToken ct)
    {
        using var process = CreateProcess(executable, args);
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
            _logger.LogError("{Exe} failed (exit {Code}): {Err}", executable, process.ExitCode, msg);
            throw new InvalidOperationException(
                "نتوانستیم اطلاعات ویدیو را دریافت کنیم. لطفاً لینک را بررسی کنید.");
        }

        return sb.ToString().Trim();
    }

    private Process CreateProcess(string executable, List<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = executable,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        return new Process { StartInfo = psi, EnableRaisingEvents = true };
    }

    private async Task<string?> DownloadInstagramAsync(DownloadJob job, CancellationToken ct)
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(_opts.CookiesFilePath) && File.Exists(_opts.CookiesFilePath))
        {
            args.Add("--cookies");
            args.Add(_opts.CookiesFilePath);
        }

        args.AddRange([
            "-D", _opts.DownloadDirectory,
            "-o", $"filename={job.Id}.{{extension}}",
            job.Url
        ]);

        _logger.LogInformation("Starting Instagram download via gallery-dl for job {JobId}", job.Id);

        job.Progress = 50;
        job.RaiseProgressChanged();

        using var process = CreateProcess("gallery-dl", args);
        process.Start();
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            return null;
        }

        return Directory
            .EnumerateFiles(_opts.DownloadDirectory, $"{job.Id}.*")
            .FirstOrDefault();
    }

    private VideoInfo ParseGalleryDlInfo(string originalUrl, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        
        if (root.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("فرمت اطلاعات دریافت شده نامعتبر است.");

        string id = "";
        string username = "";
        string fullname = "";
        string type = "post";

        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 2)
                continue;

            var code = item[0].GetInt32();
            if (code == 2)
            {
                var dict = item[1];
                if (dict.TryGetProperty("user", out var userEl))
                {
                    if (userEl.TryGetProperty("username", out var u)) username = u.GetString() ?? "";
                }
                else if (dict.TryGetProperty("username", out var u))
                {
                    username = u.GetString() ?? "";
                }
                
                if (dict.TryGetProperty("fullname", out var f)) fullname = f.GetString() ?? "";
            }
            else if (code == 3)
            {
                var dict = item[2];
                if (dict.TryGetProperty("media_id", out var mid)) id = mid.GetString() ?? "";
                if (dict.TryGetProperty("type", out var t)) type = t.GetString() ?? "";
            }
        }

        if (string.IsNullOrEmpty(id))
            id = Guid.NewGuid().ToString("N");

        var uploader = !string.IsNullOrEmpty(fullname) ? $"{fullname} (@{username})" : $"@{username}";
        var title = $"{type.ToUpper()} from {uploader}";

        return new VideoInfo
        {
            Id = id,
            Title = title,
            ThumbnailUrl = null,
            Duration = TimeSpan.Zero,
            Uploader = uploader,
            OriginalUrl = originalUrl,
            Platform = Platform.Instagram,
            AvailableQualities = new List<VideoQuality> { VideoQuality.Best }
        };
    }

    private List<string> BuildBaseArgs(string url)
    {
        var args = new List<string>();
        var platform = DetectPlatform(url);

        if (platform == Platform.YouTube)
        {
            if (!string.IsNullOrWhiteSpace(_opts.ProxyUrl))
            {
                args.Add("--proxy");
                args.Add(_opts.ProxyUrl);
            }

            // PO Token sidecar: passes the bgutil provider base URL as an extractor-arg.
            if (!string.IsNullOrWhiteSpace(_opts.PotProviderUrl))
            {
                args.Add("--extractor-args");
                args.Add($"youtube:getpot_bgutil_baseurl={_opts.PotProviderUrl}");
            }
        }
        else if (platform == Platform.Instagram)
        {
            if (!string.IsNullOrWhiteSpace(_opts.CookiesFilePath) &&
                File.Exists(_opts.CookiesFilePath) &&
                new FileInfo(_opts.CookiesFilePath).Length > 0)
            {
                args.Add("--cookies");
                args.Add(_opts.CookiesFilePath);
            }
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
