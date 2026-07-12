using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using YTDLHub.Bot.Services;
using YTDLHub.Core.Enums;
using YTDLHub.Core.Interfaces;
using YTDLHub.Core.Models;
using VideoQuality = YTDLHub.Core.Enums.VideoQuality;

namespace YTDLHub.Bot.Handlers;

/// <summary>
/// Handles the inline keyboard callback when a user selects a download quality.
/// Pre-checks the estimated file size BEFORE starting any download.
/// If the estimated size exceeds 50 MB, aborts immediately and redirects the user
/// to the web panel — no download is ever started.
/// For files within the limit, streams progress updates and sends the file.
/// </summary>
public sealed class CallbackHandler
{
    private const long MaxTelegramFileSizeBytes = 50 * 1024 * 1024; // 50 MB

    private readonly IDownloadService _downloader;
    private readonly UserStateService _userState;
    private readonly ILogger<CallbackHandler> _logger;
    private readonly IUserService _userService;

    public CallbackHandler(
        IDownloadService downloader,
        UserStateService userState,
        ILogger<CallbackHandler> logger,
        IUserService userService)
    {
        _downloader = downloader;
        _userState  = userState;
        _logger     = logger;
        _userService = userService;
    }

    public async Task HandleAsync(ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        var data   = callback.Data ?? string.Empty;
        var chatId = callback.Message!.Chat.Id;
        _logger.LogInformation("Callback received for chat {ChatId}, data: {Data}", chatId, data);

        // Answer the callback to remove the "loading" spinner on the button
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        string formatId = "";
        VideoQuality quality = VideoQuality.Best;

        if (data.StartsWith("dl:yt:"))
        {
            formatId = data["dl:yt:".Length..];
        }
        else
        {
            // Parse callback data: "dl:{VideoQuality}"
            if (!data.StartsWith("dl:") ||
                !Enum.TryParse<VideoQuality>(data["dl:".Length..], out quality))
            {
                await bot.SendMessage(chatId, "\u26a0\ufe0f \u062f\u0631\u062e\u0648\u0627\u0633\u062a \u0646\u0627\u0645\u0639\u062a\u0628\u0631 \u0627\u0633\u062a.", cancellationToken: ct);
                return;
            }
        }

        // Retrieve the URL and cached VideoInfo stored when the user sent the link
        if (!_userState.TryGetPendingUrl(chatId, out var state))
        {
            await bot.SendMessage(
                chatId,
                "\u23f0 \u0644\u06cc\u0646\u06a9 \u0645\u0646\u0642\u0636\u06cc \u0634\u062f\u0647. \u0644\u0637\u0641\u0627\u064b \u062f\u0648\u0628\u0627\u0631\u0647 \u0644\u06cc\u0646\u06a9 \u0631\u0627 \u0627\u0631\u0633\u0627\u0644 \u06a9\u0646\u06cc\u062f.",
                cancellationToken: ct);
            return;
        }

        // ── Pre-download size estimation ────────────────────────────────────
        // Use the cached VideoInfo (already fetched by MessageHandler) to check
        // the estimated file size BEFORE starting a download.
        long estimatedBytes = EstimateFileSize(state.Info, formatId, quality);

        if (estimatedBytes > MaxTelegramFileSizeBytes)
        {
            _userState.Clear(chatId);
            var estimatedMb = estimatedBytes / (1024.0 * 1024.0);
            _logger.LogWarning(
                "Pre-download check: estimated size {Size:F1} MB exceeds 50 MB limit for chat {ChatId}. Aborting.",
                estimatedMb, chatId);

            // Remove the quality keyboard so the user cannot retry the same selection
            try
            {
                await bot.EditMessageReplyMarkup(
                    chatId,
                    callback.Message.MessageId,
                    replyMarkup: null,
                    cancellationToken: ct);
            }
            catch { /* ignore */ }

            await bot.SendMessage(
                chatId,
                $"\U0001f6ab \u0627\u06cc\u0646 \u0641\u0627\u06cc\u0644 \u0628\u0631\u0627\u06cc \u0627\u0631\u0633\u0627\u0644 \u0627\u0632 \u0637\u0631\u06cc\u0642 \u062a\u0644\u06af\u0631\u0627\u0645 \u062e\u06cc\u0644\u06cc \u0628\u0632\u0631\u06af \u0627\u0633\u062a.\n" +
                $"\U0001f4e6 \u062d\u062c\u0645 \u062a\u062e\u0645\u06cc\u0646\u06cc: {estimatedMb:F1} MB (\u062d\u062f \u0645\u062c\u0627\u0632: 50 MB)\n\n" +
                "\u0628\u0631\u0627\u06cc \u062f\u0627\u0646\u0644\u0648\u062f \u0627\u06cc\u0646 \u0641\u0627\u06cc\u0644\u060c \u0644\u0637\u0641\u0627\u064b \u0628\u0647 \u0648\u0628\u200c\u067e\u0646\u0644 \u0648\u0627\u0631\u062f \u0634\u0648\u06cc\u062f.",
                cancellationToken: ct);
            return;
        }
        // ───────────────────────────────────────────────────────────────────

        _userState.Clear(chatId);

        // Remove quality keyboard from the previous message
        try
        {
            await bot.EditMessageReplyMarkup(
                chatId,
                callback.Message.MessageId,
                replyMarkup: null,
                cancellationToken: ct);
        }
        catch { /* ignore if message was already modified */ }

        // Send a progress message we'll edit as download progresses
        var progressMsg = await bot.SendMessage(
            chatId,
            $"\u23f3 \u062f\u0631 \u062d\u0627\u0644 \u062f\u0627\u0646\u0644\u0648\u062f: {quality.ToDisplayName()}\n\u25b1\u25b1\u25b1\u25b1\u25b1\u25b1\u25b1\u25b1\u25b1\u25b1 0%",
            cancellationToken: ct);

        DownloadJob? job = null;
        try
        {
            var user = await _userService.GetUserByTelegramIdAsync(chatId);
            job = await _downloader.StartDownloadAsync(state.Url, quality, formatId, user?.Id, null, ct);
            job.VideoTitle = state.Info.Title;

            // Keep editing progress message until done
            await TrackProgressAsync(bot, chatId, progressMsg.MessageId, job, ct);

            if (job.Status == JobStatus.Failed)
            {
                await bot.EditMessageText(
                    chatId,
                    progressMsg.MessageId,
                    $"\u274c \u062f\u0627\u0646\u0644\u0648\u062f \u0646\u0627\u0645\u0648\u0641\u0642 \u0628\u0648\u062f.\n{job.ErrorMessage}",
                    cancellationToken: ct);
                return;
            }

            // Delete progress message; now send the actual file
            await bot.DeleteMessage(chatId, progressMsg.MessageId, ct);

            if (job.FileSizeBytes <= MaxTelegramFileSizeBytes)
            {
                _logger.LogInformation("Sending file to chat {ChatId}, file: {File}, size: {Size} bytes", chatId, job.FileName, job.FileSizeBytes);
                await SendFileAsync(bot, chatId, job, ct);
                _logger.LogInformation("Successfully sent file to chat {ChatId}", chatId);
            }
            else
            {
                // Safety net: post-download check (in case estimation was unavailable)
                _logger.LogWarning("Post-download: file size {Size} bytes exceeds Telegram limit", job.FileSizeBytes);
                var sizeMb = job.FileSizeBytes / (1024.0 * 1024.0);
                await bot.SendMessage(
                    chatId,
                    $"\u26a0\ufe0f \u062d\u062c\u0645 \u0641\u0627\u06cc\u0644 ({sizeMb:F1} MB) \u0627\u0632 \u062d\u062f \u0645\u062c\u0627\u0632 \u062a\u0644\u06af\u0631\u0627\u0645 (50 MB) \u0628\u06cc\u0634\u062a\u0631 \u0627\u0633\u062a.\n" +
                    "\u0628\u0631\u0627\u06cc \u062f\u0627\u0646\u0644\u0648\u062f \u0641\u0627\u06cc\u0644\u200c\u0647\u0627\u06cc \u0628\u0632\u0631\u06af\u060c \u0627\u0632 \u0648\u0628\u200c\u067e\u0646\u0644 \u0627\u0633\u062a\u0641\u0627\u062f\u0647 \u06a9\u0646\u06cc\u062f.",
                    cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Callback download failed for chat {ChatId}", chatId);
            await bot.EditMessageText(
                chatId,
                progressMsg.MessageId,
                "\u274c \u062e\u0637\u0627\u06cc\u06cc \u0631\u062e \u062f\u0627\u062f. \u0644\u0637\u0641\u0627\u064b \u062f\u0648\u0628\u0627\u0631\u0647 \u062a\u0644\u0627\u0634 \u06a9\u0646\u06cc\u062f.",
                cancellationToken: ct);
        }
        finally
        {
            if (job?.FilePath is not null && File.Exists(job.FilePath))
            {
                try { File.Delete(job.FilePath); } catch { /* ignore */ }
            }
        }
    }

    // ── Size estimation ───────────────────────────────────────────────────────

    /// <summary>
    /// Estimates the file size in bytes for the selected format using cached VideoInfo.
    /// Returns 0 if no size information is available (fail-safe: allow download).
    /// </summary>
    private static long EstimateFileSize(VideoInfo info, string formatId, VideoQuality quality)
    {
        // YouTube: look up the specific format by formatId
        if (!string.IsNullOrEmpty(formatId) && info.YoutubeFormats.Any())
        {
            var fmt = info.YoutubeFormats.FirstOrDefault(f => f.FormatId == formatId);
            if (fmt?.FileSizeBytes.HasValue == true)
                return fmt.FileSizeBytes.Value;
        }

        // Non-YouTube or unknown: no reliable per-format size, return 0 (allow through)
        return 0;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    internal static async Task TrackProgressAsync(
        ITelegramBotClient bot,
        long chatId,
        int messageId,
        DownloadJob job,
        CancellationToken ct)
    {
        var lastEdited = string.Empty;

        // Subscribe to live events from the download job
        var tcs = new TaskCompletionSource();
        job.ProgressChanged += j =>
        {
            if (j.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                tcs.TrySetResult();
        };

        // If job finished before we subscribed, complete immediately
        if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
        {
            tcs.TrySetResult();
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var timerTask   = Task.Run(async () =>
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var text = BuildProgressText(job);
                if (text == lastEdited) continue;

                try
                {
                    await bot.EditMessageText(chatId, messageId, text, cancellationToken: ct);
                    lastEdited = text;
                }
                catch { /* Telegram throttles edits — ignore */ }

                if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                    break;
            }
        }, ct);

        // Wait for either completion event or polling loop to finish
        await Task.WhenAny(tcs.Task, timerTask);
    }

    private static string BuildProgressText(DownloadJob job)
    {
        var bar  = BuildProgressBar(job.Progress);
        var pct  = $"{job.Progress}%";
        var icon = job.Status switch
        {
            JobStatus.Merging     => "\U0001f527",
            JobStatus.Downloading => "\u2b07\ufe0f",
            JobStatus.Completed   => "\u2705",
            JobStatus.Failed      => "\u274c",
            _                     => "\u23f3"
        };

        var statusText = job.Status switch
        {
            JobStatus.Queued      => "\u062f\u0631 \u0635\u0641 \u0627\u0646\u062a\u0638\u0627\u0631...",
            JobStatus.Fetching    => "\u062f\u0631 \u062d\u0627\u0644 \u062f\u0631\u06cc\u0627\u0641\u062a \u0627\u0637\u0644\u0627\u0639\u0627\u062a...",
            JobStatus.Downloading => $"\u062f\u0631 \u062d\u0627\u0644 \u062f\u0627\u0646\u0644\u0648\u062f: {pct}",
            JobStatus.Merging     => "\u062f\u0631 \u062d\u0627\u0644 \u0627\u062f\u063a\u0627\u0645 \u0648\u06cc\u062f\u06cc\u0648 \u0648 \u0635\u062f\u0627...",
            JobStatus.Completed   => "\u062f\u0627\u0646\u0644\u0648\u062f \u06a9\u0627\u0645\u0644 \u0634\u062f \u2705",
            JobStatus.Failed      => "\u062f\u0627\u0646\u0644\u0648\u062f \u0646\u0627\u0645\u0648\u0641\u0642 \u0628\u0648\u062f \u274c",
            JobStatus.Cancelled   => "\u0644\u063a\u0648 \u0634\u062f",
            _                     => "..."
        };

        var speedInfo = job.Status == JobStatus.Downloading && !string.IsNullOrWhiteSpace(job.SpeedDisplay)
            ? $"\n\U0001f680 \u0633\u0631\u0639\u062a: {job.SpeedDisplay}  |  \u23f1\ufe0f \u0632\u0645\u0627\u0646 \u0628\u0627\u0642\u06cc: {job.EtaDisplay}"
            : string.Empty;

        return $"{icon} {statusText}\n{bar} {pct}{speedInfo}";
    }

    private static string BuildProgressBar(int percent)
    {
        const int barLen = 10;
        var filled = (int)Math.Round(percent / 100.0 * barLen);
        return new string('\u25b0', filled) + new string('\u25b1', barLen - filled);
    }

    internal static async Task SendFileAsync(
        ITelegramBotClient bot,
        long chatId,
        DownloadJob job,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(job.FilePath!);
        var inputFile = new InputFileStream(stream, job.FileName ?? "video.mp4");
        var ext       = Path.GetExtension(job.FileName ?? "").ToLowerInvariant();

        if (ext is ".mp3" or ".m4a" or ".ogg")
        {
            await bot.SendAudio(
                chatId:            chatId,
                audio:             inputFile,
                caption:           $"\U0001f3b5 {job.VideoTitle ?? "\u0641\u0627\u06cc\u0644 \u0635\u0648\u062a\u06cc"}",
                cancellationToken: ct);
        }
        else if (ext is ".jpg" or ".jpeg" or ".png" or ".webp")
        {
            await bot.SendPhoto(
                chatId:            chatId,
                photo:             inputFile,
                caption:           $"\U0001f4f8 {job.VideoTitle ?? "\u062a\u0635\u0648\u06cc\u0631"}",
                cancellationToken: ct);
        }
        else
        {
            await bot.SendVideo(
                chatId:            chatId,
                video:             inputFile,
                caption:           $"\U0001f3ac {job.VideoTitle ?? "\u0648\u06cc\u062f\u06cc\u0648"}",
                cancellationToken: ct);
        }
    }
}
