using YTDLHub.Core.Models;

namespace YTDLHub.Core.Interfaces;

public interface IUserService
{
    /// <summary>Gets or creates a user by their Telegram ID.</summary>
    Task<AppUser> GetOrCreateUserAsync(long telegramId, string? username, string? firstName, CancellationToken ct = default);

    Task<AppUser?> GetUserByTelegramIdAsync(long telegramId, CancellationToken ct = default);
    Task<AppUser?> GetUserByIdAsync(Guid userId, CancellationToken ct = default);

    Task<List<UserFolder>> GetUserFoldersAsync(Guid userId, CancellationToken ct = default);
    Task<UserFolder> CreateFolderAsync(Guid userId, string name, string? description, CancellationToken ct = default);
    Task<UserFolder?> GetDefaultFolderAsync(Guid userId, CancellationToken ct = default);
    Task<bool> DeleteFolderAsync(Guid userId, Guid folderId, CancellationToken ct = default);
    Task UpdateLastSeenAsync(Guid userId, CancellationToken ct = default);
}
