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
/// Starts the download, streams progress updates by editing the message,
/// then sends the file (if ≤ 50 MB) or a notice for larger files.
/// </summary>
public sealed class CallbackHandler
{
    private const long MaxTelegramFileSizeBytes = 50 * 1024 * 1024; // 50 MB

    private readonly IDownloadService _downloader;
    private readonly UserStateService _userState;
    private readonly ILogger<CallbackHandler> _logger;

    public CallbackHandler(
        IDownloadService downloader,
        UserStateService userState,
        ILogger<CallbackHandler> logger)
    {
        _downloader = downloader;
        _userState  = userState;
        _logger     = logger;
    }

    public async Task HandleAsync(ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        var data   = callback.Data ?? string.Empty;
        var chatId = callback.Message!.Chat.Id;
        _logger.LogInformation("Callback received for chat {ChatId}, data: {Data}", chatId, data);

        // Answer the callback to remove the "loading" spinner on the button
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        // Parse callback data: "dl:{VideoQuality}"
        if (!data.StartsWith("dl:") ||
            !Enum.TryParse<VideoQuality>(data["dl:".Length..], out var quality))
        {
            await bot.SendMessage(chatId, "⚠️ درخواست نامعتبر است.", cancellationToken: ct);
            return;
        }

        // Retrieve the URL we stored when the user sent the link
        if (!_userState.TryGetPendingUrl(chatId, out var state))
        {
            await bot.SendMessage(
                chatId,
                "⏰ لینک منقضی شده. لطفاً دوباره لینک را ارسال کنید.",
                cancellationToken: ct);
            return;
        }

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
            $"⏳ در حال دانلود: {quality.ToDisplayName()}\n▱▱▱▱▱▱▱▱▱▱ 0%",
            cancellationToken: ct);

        DownloadJob? job = null;
        try
        {
            job = await _downloader.StartDownloadAsync(state.Url, quality, ct);

            // Keep editing progress message until done
            await TrackProgressAsync(bot, chatId, progressMsg.MessageId, job, ct);

            if (job.Status == JobStatus.Failed)
            {
                await bot.EditMessageText(
                    chatId,
                    progressMsg.MessageId,
                    $"❌ دانلود ناموفق بود.\n{job.ErrorMessage}",
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
                _logger.LogWarning("File size {Size} bytes exceeds Telegram limit of {Limit} bytes", job.FileSizeBytes, MaxTelegramFileSizeBytes);
                var sizeMb = job.FileSizeBytes / (1024.0 * 1024.0);
                await bot.SendMessage(
                    chatId,
                    $"⚠️ حجم فایل ({sizeMb:F1} MB) از حد مجاز تلگرام (50 MB) بیشتر است.\n" +
                    "برای دانلود فایل‌های بزرگ، از وب‌پنل استفاده کنید.",
                    cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Callback download failed for chat {ChatId}", chatId);
            await bot.EditMessageText(
                chatId,
                progressMsg.MessageId,
                "❌ خطایی رخ داد. لطفاً دوباره تلاش کنید.",
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

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task TrackProgressAsync(
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
            JobStatus.Merging     => "🔧",
            JobStatus.Downloading => "⬇️",
            JobStatus.Completed   => "✅",
            JobStatus.Failed      => "❌",
            _                     => "⏳"
        };

        var statusText = job.Status switch
        {
            JobStatus.Queued      => "در صف انتظار...",
            JobStatus.Fetching    => "در حال دریافت اطلاعات...",
            JobStatus.Downloading => $"در حال دانلود: {pct}",
            JobStatus.Merging     => "در حال ادغام ویدیو و صدا...",
            JobStatus.Completed   => "دانلود کامل شد ✅",
            JobStatus.Failed      => "دانلود ناموفق بود ❌",
            JobStatus.Cancelled   => "لغو شد",
            _                     => "..."
        };

        var speedInfo = job.Status == JobStatus.Downloading && !string.IsNullOrWhiteSpace(job.SpeedDisplay)
            ? $"\n🚀 سرعت: {job.SpeedDisplay}  |  ⏱️ زمان باقی: {job.EtaDisplay}"
            : string.Empty;

        return $"{icon} {statusText}\n{bar} {pct}{speedInfo}";
    }

    private static string BuildProgressBar(int percent)
    {
        const int barLen = 10;
        var filled = (int)Math.Round(percent / 100.0 * barLen);
        return new string('▰', filled) + new string('▱', barLen - filled);
    }

    private static async Task SendFileAsync(
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
                caption:           $"🎵 {job.VideoTitle ?? "فایل صوتی"}",
                cancellationToken: ct);
        }
        else if (ext is ".jpg" or ".jpeg" or ".png" or ".webp")
        {
            await bot.SendPhoto(
                chatId:            chatId,
                photo:             inputFile,
                caption:           $"📸 {job.VideoTitle ?? "تصویر"}",
                cancellationToken: ct);
        }
        else
        {
            await bot.SendVideo(
                chatId:            chatId,
                video:             inputFile,
                caption:           $"🎬 {job.VideoTitle ?? "ویدیو"}",
                cancellationToken: ct);
        }
    }
}
