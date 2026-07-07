using YTDLHub.Core.Enums;

namespace YTDLHub.Core.Models;

/// <summary>
/// A named folder that a user can create to organize their downloads.
/// Every user gets a default "Downloads" folder automatically.
/// </summary>
public class UserFolder
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public string Name { get; set; } = "Downloads";
    public string? Description { get; set; }

    /// <summary>Which platform this folder is intended for. Null = all platforms.</summary>
    public Platform? Platform { get; set; }

    public bool IsDefault { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<DownloadJob> Downloads { get; set; } = new();
}
