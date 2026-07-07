using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YTDLHub.Core.Interfaces;
using YTDLHub.Core.Models;
using YTDLHub.Infrastructure.Data;

namespace YTDLHub.Infrastructure.Services;

/// <summary>
/// Web authentication service — handles Username/Password based registration and login.
/// </summary>
public class WebAuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ILogger<WebAuthService> _logger;

    public WebAuthService(AppDbContext db, ILogger<WebAuthService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? Error)> RegisterAsync(
        string username,
        string password,
        CancellationToken ct = default)
    {
        // ── Validation ─────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            return (false, "نام کاربری باید حداقل ۳ کاراکتر داشته باشد.");

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            return (false, "رمز عبور باید حداقل ۶ کاراکتر داشته باشد.");

        // Only allow alphanumeric + underscore
        if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z0-9_]+$"))
            return (false, "نام کاربری فقط می‌تواند شامل حروف انگلیسی، اعداد و _ باشد.");

        // ── Uniqueness check ───────────────────────────────────────────────
        var exists = await _db.Users.AnyAsync(
            u => u.Username.ToLower() == username.ToLower(), ct);

        if (exists)
            return (false, "این نام کاربری قبلاً ثبت شده است.");

        // ── Create user ────────────────────────────────────────────────────
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

        var user = new AppUser
        {
            Username     = username,
            PasswordHash = passwordHash,
            IsProfileComplete = false,
        };

        _db.Users.Add(user);

        // Create default folder
        var defaultFolder = new UserFolder
        {
            UserId    = user.Id,
            Name      = "دانلودها",
            IsDefault = true,
        };
        _db.Folders.Add(defaultFolder);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("New web user registered: {Username} (Id={UserId})", username, user.Id);
        return (true, null);
    }

    /// <inheritdoc />
    public async Task<AppUser?> LoginAsync(
        string username,
        string password,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return null;

        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.Username.ToLower() == username.ToLower(), ct);

        if (user == null) return null;

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            _logger.LogWarning("Failed login attempt for username: {Username}", username);
            return null;
        }

        user.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return user;
    }
}
