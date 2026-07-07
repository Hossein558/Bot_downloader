namespace YTDLHub.Core.Models;

/// <summary>
/// Represents a registered user authenticated via Telegram.
/// </summary>
public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Telegram numeric user ID (unique identifier)</summary>
    public long TelegramId { get; set; }

    public string? TelegramUsername { get; set; }
    public string? PhoneNumber { get; set; }
    public string DisplayName { get; set; } = "User";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<UserFolder> Folders { get; set; } = new();
    public List<DownloadJob> Downloads { get; set; } = new();
}
