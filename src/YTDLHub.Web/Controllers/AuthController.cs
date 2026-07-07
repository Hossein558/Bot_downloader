using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using YTDLHub.Core.Interfaces;

namespace YTDLHub.Web.Controllers;

[Route("auth")]
public class AuthController : Controller
{
    private readonly IAuthService _authService;
    private readonly IUserService _userService;

    public AuthController(IAuthService authService, IUserService userService)
    {
        _authService = authService;
        _userService = userService;
    }

    // ── Sign-in (called after Blazor login page validates credentials) ──────
    [HttpGet("signin")]
    public async Task<IActionResult> SignIn([FromQuery] Guid userId)
    {
        var user = await _userService.GetUserByIdAsync(userId);
        if (user == null) return Redirect("/login");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("DisplayName", user.DisplayName),
            new("IsProfileComplete", user.IsProfileComplete.ToString()),
        };

        var identity   = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal  = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc   = DateTimeOffset.UtcNow.AddDays(30)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties);

        // Redirect based on profile completion
        return user.IsProfileComplete
            ? Redirect("/")
            : Redirect("/profile/setup");
    }

    // ── Refresh claims (after profile update) ──────────────────────────────
    [HttpGet("refresh")]
    public async Task<IActionResult> Refresh()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return Redirect("/login");

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Redirect("/login");

        var user = await _userService.GetUserByIdAsync(userId);
        if (user == null) return Redirect("/login");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("DisplayName", user.DisplayName),
            new("IsProfileComplete", user.IsProfileComplete.ToString()),
        };

        var identity   = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal  = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc   = DateTimeOffset.UtcNow.AddDays(30)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties);

        return Redirect("/");
    }

    // ── Logout ─────────────────────────────────────────────────────────────
    [HttpGet("/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/login");
    }
}
