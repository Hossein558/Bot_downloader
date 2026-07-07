using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using YTDLHub.Bot.Services;
using YTDLHub.Core.Enums;
using YTDLHub.Core.Interfaces;
using YTDLHub.Core.Models;
using VideoQuality = YTDLHub.Core.Enums.VideoQuality;

namespace YTDLHub.Bot.Handlers;

/// <summary>
/// Handles incoming text messages that contain a YouTube or Instagram URL.
/// Fetches video info via yt-dlp and presents a quality selection keyboard.
/// </summary>
public sealed class MessageHandler
{
    private static readonly System.Text.RegularExpressions.Regex UrlRegex = new(
        @"https?://\S+",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly IDownloadService _downloader;
    private readonly UserStateService _userState;
    private readonly ILogger<MessageHandler> _logger;
    private readonly IUserService _userService;

    public MessageHandler(
        IDownloadService downloader,
        UserStateService userState,
        ILogger<MessageHandler> logger,
        IUserService userService)
    {
        _downloader = downloader;
        _userState  = userState;
        _logger     = logger;
        _userService = userService;
    }

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var text = message.Text ?? string.Empty;
        _logger.LogInformation("Received message from Chat ID: {ChatId}, Text: {Text}", message.Chat.Id, text);
        var match = UrlRegex.Match(text);

        if (!match.Success)
        {
            await bot.SendMessage(
                message.Chat.Id,
                "❌ لطفاً یک لینک معتبر از یوتیوب یا اینستاگرام بفرست.",
                cancellationToken: ct);
            return;
        }

        var url      = match.Value.Trim();
        var platform = _downloader.DetectPlatform(url);

        if (platform == Platform.Unknown)
        {
            await bot.SendMessage(
                message.Chat.Id,
                "⚠️ فقط لینک‌های یوتیوب و اینستاگرام پشتیبانی می‌شوند.",
                cancellationToken: ct);
            return;
        }

        // Show general "fetching…" message
        var fetchingMsg = await bot.SendMessage(
            message.Chat.Id,
            $"🔍 در حال دریافت اطلاعات...",
            cancellationToken: ct);

        try
        {
            if (platform == Platform.Instagram)
            {
                // Update fetching message to show direct download status
                await bot.EditMessageText(
                    message.Chat.Id,
                    fetchingMsg.MessageId,
                    "⏳ در حال دانلود تصویر/ویدیو از اینستاگرام...",
                    cancellationToken: ct);

                DownloadJob? job = null;
                try
                {
                    var info = await _downloader.GetVideoInfoAsync(url, ct);
                    
                    var user = await _userService.GetUserByTelegramIdAsync(message.Chat.Id);
                    job = await _downloader.StartDownloadAsync(url, VideoQuality.Best, null, user?.Id, null, ct);
                    job.VideoTitle = info.Title;

                    // Track progress using CallbackHandler helper
                    await CallbackHandler.TrackProgressAsync(bot, message.Chat.Id, fetchingMsg.MessageId, job, ct);

                    if (job.Status == JobStatus.Failed)
                    {
                        await bot.EditMessageText(
                            message.Chat.Id,
                            fetchingMsg.MessageId,
                            $"❌ دانلود ناموفق بود.\n{job.ErrorMessage}",
                            cancellationToken: ct);
                        return;
                    }

                    // Delete progress message and send the actual file
                    await bot.DeleteMessage(message.Chat.Id, fetchingMsg.MessageId, ct);
                    await CallbackHandler.SendFileAsync(bot, message.Chat.Id, job, ct);
                }
                finally
                {
                    if (job?.FilePath is not null && File.Exists(job.FilePath))
                    {
                        try { File.Delete(job.FilePath); } catch { /* ignore */ }
                    }
                }
                return;
            }

            // Platform is YouTube
            var infoCard = await _downloader.GetVideoInfoAsync(url, ct);
            _userState.SetPendingUrl(message.Chat.Id, url, infoCard);

            // Build inline keyboard – custom dynamic formats for YouTube
            var rows = new List<InlineKeyboardButton[]>();
            if (infoCard.YoutubeFormats.Any())
            {
                foreach (var fmt in infoCard.YoutubeFormats)
                {
                    rows.Add(new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            text:         fmt.DisplayName,
                            callbackData: $"dl:yt:{fmt.FormatId}")
                    });
                }
            }
            else
            {
                // Fallback quality presets
                foreach (var q in infoCard.AvailableQualities)
                {
                    rows.Add(new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            text:         q.ToDisplayName(),
                            callbackData: $"dl:{q}")
                    });
                }
            }

            var keyboard = new InlineKeyboardMarkup(rows);

            var duration = infoCard.Duration > TimeSpan.Zero
                ? $"{(int)infoCard.Duration.TotalMinutes}:{infoCard.Duration.Seconds:D2}"
                : "نامشخص";

            var caption =
                $"▶️ *{EscapeMd(infoCard.Title)}*\n" +
                $"👤 {EscapeMd(infoCard.Uploader)}\n" +
                $"⏱️ مدت: {duration}\n\n" +
                $"⬇️ کیفیت مورد نظر را انتخاب کنید:";

            // Delete "fetching" placeholder and send the real card
            await bot.DeleteMessage(message.Chat.Id, fetchingMsg.MessageId, ct);

            if (!string.IsNullOrWhiteSpace(infoCard.ThumbnailUrl))
            {
                await bot.SendPhoto(
                    chatId:            message.Chat.Id,
                    photo:             new InputFileUrl(infoCard.ThumbnailUrl),
                    caption:           caption,
                    parseMode:         ParseMode.MarkdownV2,
                    replyMarkup:       keyboard,
                    cancellationToken: ct);
            }
            else
            {
                await bot.SendMessage(
                    chatId:            message.Chat.Id,
                    text:              caption,
                    parseMode:         ParseMode.MarkdownV2,
                    replyMarkup:       keyboard,
                    cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message for {Url}", url);

            await bot.EditMessageText(
                message.Chat.Id,
                fetchingMsg.MessageId,
                "❌ خطایی رخ داد. لطفا لینک را بررسی کرده و مجدداً تلاش کنید.",
                cancellationToken: ct);
        }
    }

    private static string EscapeMd(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"([_*\[\]()~`>#+\-=|{}.!\\])", @"\$1");
}
