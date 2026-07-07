namespace YTDLHub.Core.Models;

/// <summary>
/// Represents a registered user of the YTDLHub platform.
/// Users can register independently via the web panel using Username/Password.
/// Telegram integration is optional.
/// </summary>
public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ── Web Authentication ─────────────────────────────────────────────────
    /// <summary>Unique username for web login (min 3 chars)</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>BCrypt hashed password</summary>
    public string PasswordHash { get; set; } = string.Empty;

    // ── Profile Info ───────────────────────────────────────────────────────
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>Display name shown in the UI (auto-generated from first/last name or username)</summary>
    public string DisplayName => !string.IsNullOrEmpty(FirstName)
        ? $"{FirstName} {LastName}".Trim()
        : Username;

    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }

    // ── Telegram (optional) ────────────────────────────────────────────────
    /// <summary>Optional: Telegram numeric user ID (linked account)</summary>
    public long? TelegramId { get; set; }

    public string? TelegramUsername { get; set; }

    // ── Profile Completion ─────────────────────────────────────────────────
    /// <summary>
    /// True when user has filled all required profile fields.
    /// Required: FirstName, LastName, PhoneNumber, AND (Email OR TelegramId/TelegramUsername).
    /// Download is blocked until this is true.
    /// </summary>
    public bool IsProfileComplete { get; set; } = false;

    // ── Timestamps ─────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ─────────────────────────────────────────────────────────
    public List<UserFolder> Folders { get; set; } = new();
    public List<DownloadJob> Downloads { get; set; } = new();

    // ── Helper ─────────────────────────────────────────────────────────────
    /// <summary>Checks if all required profile fields are filled.</summary>
    public bool CheckProfileComplete()
    {
        bool hasBasic = !string.IsNullOrWhiteSpace(FirstName)
                     && !string.IsNullOrWhiteSpace(LastName)
                     && !string.IsNullOrWhiteSpace(PhoneNumber);

        bool hasContact = !string.IsNullOrWhiteSpace(Email)
                       || TelegramId.HasValue
                       || !string.IsNullOrWhiteSpace(TelegramUsername);

        return hasBasic && hasContact;
    }
}
