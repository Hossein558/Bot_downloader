using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace YTDLHub.Bot.Handlers;

/// <summary>
/// Handles the /start and /help commands.
/// </summary>
public static class StartHandler
{
    public static async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var welcomeText = """
            👋 *سلام\! خوش اومدی به YTDLHub Bot*

            این ربات بهت کمک می‌کنه تا ویدیوها رو از یوتیوب و اینستاگرام دانلود کنی\.

            *نحوه استفاده:*
            ۱\. لینک ویدیوی یوتیوب یا اینستاگرام رو بفرست
            ۲\. کیفیت مورد نظرت رو انتخاب کن
            ۳\. فایل دانلود می‌شه و برات ارسال می‌شه

            *پشتیبانی از:*
            ▶️ یوتیوب \(ویدیو، Shorts\)
            📸 اینستاگرام \(ریلز، پست، استوری\)

            لینک رو بفرست تا شروع کنیم\! 🚀
            """;

        await bot.SendMessage(
            chatId:    message.Chat.Id,
            text:      welcomeText,
            parseMode: ParseMode.MarkdownV2,
            cancellationToken: ct);
    }
}
