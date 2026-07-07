namespace YTDLHub.Core.Models;

/// <summary>
/// A one-time password (OTP) code used for web panel authentication via Telegram.
/// </summary>
public class LoginOtp
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public long TelegramId { get; set; }

    /// <summary>6-digit numeric code sent to the user via Telegram bot.</summary>
    public string Code { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(5);
    public bool IsUsed { get; set; } = false;
}
