using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using YTDLHub.Core.Interfaces;
using YTDLHub.Core.Models;
using YTDLHub.Infrastructure.Data;

namespace YTDLHub.Infrastructure.Services;

public class TelegramAuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<TelegramAuthService> _logger;

    public TelegramAuthService(AppDbContext db, ITelegramBotClient bot, ILogger<TelegramAuthService> logger)
    {
        _db     = db;
        _bot    = bot;
        _logger = logger;
    }

    public async Task<bool> SendOtpAsync(long telegramId, CancellationToken ct = default)
    {
        // Clean up old codes for this user
        var oldCodes = await _db.LoginOtps
            .Where(o => o.TelegramId == telegramId && !o.IsUsed)
            .ToListAsync(ct);
        _db.LoginOtps.RemoveRange(oldCodes);

        // Generate 6-digit code
        var code = Random.Shared.Next(100_000, 999_999).ToString();

        var otp = new LoginOtp
        {
            TelegramId = telegramId,
            Code       = code,
            ExpiresAt  = DateTime.UtcNow.AddMinutes(5)
        };
        _db.LoginOtps.Add(otp);
        await _db.SaveChangesAsync(ct);

        try
        {
            await _bot.SendMessage(
                chatId: telegramId,
                text: $"🔐 *کد ورود به پنل YTDLHub*\n\n" +
                      $"کد تأیید شما: `{code}`\n\n" +
                      $"⏱ این کد تا ۵ دقیقه معتبر است.\n" +
                      $"اگر این درخواست از شما نیست، آن را نادیده بگیرید.",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send OTP to TelegramId={TelegramId}. User may have never started the bot.", telegramId);
            return false;
        }
    }

    public async Task<long?> VerifyOtpAsync(long telegramId, string code, CancellationToken ct = default)
    {
        var otp = await _db.LoginOtps.FirstOrDefaultAsync(
            o => o.TelegramId == telegramId &&
                 o.Code == code &&
                 !o.IsUsed &&
                 o.ExpiresAt > DateTime.UtcNow,
            ct);

        if (otp == null) return null;

        otp.IsUsed = true;
        await _db.SaveChangesAsync(ct);

        return telegramId;
    }
}
