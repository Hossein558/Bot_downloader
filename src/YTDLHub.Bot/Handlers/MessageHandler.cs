using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using YTDLHub.Bot.Services;
using YTDLHub.Core.Enums;
using YTDLHub.Core.Interfaces;

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

    public MessageHandler(
        IDownloadService downloader,
        UserStateService userState,
        ILogger<MessageHandler> logger)
    {
        _downloader = downloader;
        _userState  = userState;
        _logger     = logger;
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

        // Show "fetching…" message while we call yt-dlp
        var fetchingMsg = await bot.SendMessage(
            message.Chat.Id,
            $"🔍 در حال دریافت اطلاعات ویدیو...",
            cancellationToken: ct);

        try
        {
            var info = await _downloader.GetVideoInfoAsync(url, ct);
            _userState.SetPendingUrl(message.Chat.Id, url, info);

            // Build inline keyboard – one button per available quality
            var rows = info.AvailableQualities
                .Select(q => new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        text:         q.ToDisplayName(),
                        callbackData: $"dl:{q}")
                })
                .ToArray();

            var keyboard = new InlineKeyboardMarkup(rows);

            var duration = info.Duration > TimeSpan.Zero
                ? $"{(int)info.Duration.TotalMinutes}:{info.Duration.Seconds:D2}"
                : "نامشخص";

            var platformIcon = platform == Platform.YouTube ? "▶️" : "📸";
            var caption =
                $"{platformIcon} *{EscapeMd(info.Title)}*\n" +
                $"👤 {EscapeMd(info.Uploader)}\n" +
                $"⏱️ مدت: {duration}\n\n" +
                $"⬇️ کیفیت مورد نظر را انتخاب کنید:";

            // Delete "fetching" placeholder and send the real card
            await bot.DeleteMessage(message.Chat.Id, fetchingMsg.MessageId, ct);

            if (!string.IsNullOrWhiteSpace(info.ThumbnailUrl))
            {
                await bot.SendPhoto(
                    chatId:            message.Chat.Id,
                    photo:             new InputFileUrl(info.ThumbnailUrl),
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
            _logger.LogError(ex, "Failed to fetch info for {Url}", url);

            await bot.EditMessageText(
                message.Chat.Id,
                fetchingMsg.MessageId,
                "❌ نتوانستیم اطلاعات ویدیو را دریافت کنیم.\n" +
                "• لینک را بررسی کنید\n" +
                "• محتوا ممکن است پرایوت باشد",
                cancellationToken: ct);
        }
    }

    private static string EscapeMd(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"([_*\[\]()~`>#+\-=|{}.!\\])", @"\$1");
}
