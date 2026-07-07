using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YTDLHub.Core.Enums;
using YTDLHub.Core.Interfaces;
using YTDLHub.Core.Models;
using YTDLHub.Infrastructure.Data;

namespace YTDLHub.Infrastructure.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _db;
    private readonly ILogger<UserService> _logger;

    public UserService(AppDbContext db, ILogger<UserService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AppUser> GetOrCreateUserAsync(long telegramId, string? username, string? firstName, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.Folders)
            .FirstOrDefaultAsync(u => u.TelegramId == telegramId, ct);

        if (user != null)
        {
            // Update display info
            if (!string.IsNullOrEmpty(username)) user.TelegramUsername = username;
            if (!string.IsNullOrEmpty(firstName)) user.DisplayName = firstName;
            user.LastSeenAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return user;
        }

        // Create new user
        user = new AppUser
        {
            TelegramId      = telegramId,
            TelegramUsername = username,
            DisplayName     = firstName ?? $"User_{telegramId}",
        };

        _db.Users.Add(user);

        // Create default "Downloads" folder
        var defaultFolder = new UserFolder
        {
            UserId    = user.Id,
            Name      = "Downloads",
            IsDefault = true,
        };
        _db.Folders.Add(defaultFolder);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created new user {UserId} for TelegramId={TelegramId}", user.Id, telegramId);

        return user;
    }

    public async Task<AppUser?> GetUserByTelegramIdAsync(long telegramId, CancellationToken ct = default)
        => await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId, ct);

    public async Task<AppUser?> GetUserByIdAsync(Guid userId, CancellationToken ct = default)
        => await _db.Users.FindAsync([userId], ct);

    public async Task<List<UserFolder>> GetUserFoldersAsync(Guid userId, CancellationToken ct = default)
        => await _db.Folders
            .Where(f => f.UserId == userId)
            .OrderBy(f => !f.IsDefault)
            .ThenBy(f => f.CreatedAt)
            .ToListAsync(ct);

    public async Task<UserFolder?> GetDefaultFolderAsync(Guid userId, CancellationToken ct = default)
        => await _db.Folders.FirstOrDefaultAsync(f => f.UserId == userId && f.IsDefault, ct);

    public async Task<UserFolder> CreateFolderAsync(Guid userId, string name, string? description, CancellationToken ct = default)
    {
        var folder = new UserFolder
        {
            UserId      = userId,
            Name        = name,
            Description = description,
            IsDefault   = false
        };
        _db.Folders.Add(folder);
        await _db.SaveChangesAsync(ct);
        return folder;
    }

    public async Task<bool> DeleteFolderAsync(Guid userId, Guid folderId, CancellationToken ct = default)
    {
        var folder = await _db.Folders.FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId && !f.IsDefault, ct);
        if (folder == null) return false;
        _db.Folders.Remove(folder);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task UpdateLastSeenAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user != null)
        {
            user.LastSeenAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }
}
