namespace YTDLHub.Core.Interfaces;

/// <summary>
/// Web authentication service — Username/Password based registration and login.
/// Telegram OTP is kept separately in the bot layer.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Registers a new user with the given username and plain-text password.
    /// Returns (success=true, error=null) on success,
    /// or (success=false, error="reason") if username is taken or validation fails.
    /// </summary>
    Task<(bool Success, string? Error)> RegisterAsync(
        string username,
        string password,
        CancellationToken ct = default);

    /// <summary>
    /// Validates username + password and returns the matching AppUser, or null if invalid.
    /// </summary>
    Task<Core.Models.AppUser?> LoginAsync(
        string username,
        string password,
        CancellationToken ct = default);
}
