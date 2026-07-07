namespace YTDLHub.Core.Interfaces;

public interface IAuthService
{
    /// <summary>
    /// Generates an OTP and sends it via the Telegram bot to the specified Telegram user.
    /// Returns true if the message was sent successfully, false otherwise (e.g., user never started the bot).
    /// </summary>
    Task<bool> SendOtpAsync(long telegramId, CancellationToken ct = default);

    /// <summary>
    /// Validates the OTP code entered on the web panel.
    /// Returns the Telegram ID of the verified user, or null if invalid/expired.
    /// </summary>
    Task<long?> VerifyOtpAsync(long telegramId, string code, CancellationToken ct = default);
}
