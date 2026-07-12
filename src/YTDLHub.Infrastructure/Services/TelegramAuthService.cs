using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YTDLHub.Core.Interfaces;
using YTDLHub.Core.Models;
using YTDLHub.Infrastructure.Data;

namespace YTDLHub.Infrastructure.Services;

/// <summary>
/// Web authentication service — handles Username/Password based registration and login.
/// On successful registration, seeds the new user with default platform folders:
/// "YouTube", "Instagram", and a general "دانلودها" (catch-all).
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
            return (false, "\u0646\u0627\u0645 \u06a9\u0627\u0631\u0628\u0631\u06cc \u0628\u0627\u06cc\u062f \u062d\u062f\u0627\u0642\u0644 \u06f3 \u06a9\u0627\u0631\u0627\u06a9\u062a\u0631 \u062f\u0627\u0634\u062a\u0647 \u0628\u0627\u0634\u062f.");

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            return (false, "\u0631\u0645\u0632 \u0639\u0628\u0648\u0631 \u0628\u0627\u06cc\u062f \u062d\u062f\u0627\u0642\u0644 \u06f6 \u06a9\u0627\u0631\u0627\u06a9\u062a\u0631 \u062f\u0627\u0634\u062a\u0647 \u0628\u0627\u0634\u062f.");

        // Only allow alphanumeric + underscore
        if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z0-9_]+$"))
            return (false, "\u0646\u0627\u0645 \u06a9\u0627\u0631\u0628\u0631\u06cc \u0641\u0642\u0637 \u0645\u06cc\u200c\u062a\u0648\u0627\u0646\u062f \u0634\u0627\u0645\u0644 \u062d\u0631\u0648\u0641 \u0627\u0646\u06af\u0644\u06cc\u0633\u06cc\u060c \u0627\u0639\u062f\u0627\u062f \u0648 _ \u0628\u0627\u0634\u062f.");

        // ── Uniqueness check ───────────────────────────────────────────────
        var exists = await _db.Users.AnyAsync(
            u => u.Username.ToLower() == username.ToLower(), ct);

        if (exists)
            return (false, "\u0627\u06cc\u0646 \u0646\u0627\u0645 \u06a9\u0627\u0631\u0628\u0631\u06cc \u0642\u0628\u0644\u0627\u064b \u062b\u0628\u062a \u0634\u062f\u0647 \u0627\u0633\u062a.");

        // ── Create user ────────────────────────────────────────────────────
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

        var user = new AppUser
        {
            Username         = username,
            PasswordHash     = passwordHash,
            IsProfileComplete = false,
        };

        _db.Users.Add(user);

        // ── Seed default platform folders ──────────────────────────────────
        // General catch-all (marked IsDefault so existing routing logic still works)
        _db.Folders.Add(new UserFolder
        {
            UserId    = user.Id,
            Name      = "\u062f\u0627\u0646\u0644\u0648\u062f\u0647\u0627",
            IsDefault = true,
        });

        // YouTube auto-routing folder
        _db.Folders.Add(new UserFolder
        {
            UserId    = user.Id,
            Name      = "YouTube",
            IsDefault = false,
        });

        // Instagram auto-routing folder
        _db.Folders.Add(new UserFolder
        {
            UserId    = user.Id,
            Name      = "Instagram",
            IsDefault = false,
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "New web user registered: {Username} (Id={UserId}) — seeded 3 default folders.",
            username, user.Id);

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
