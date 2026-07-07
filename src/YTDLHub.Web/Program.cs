using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using YTDLHub.Bot.Handlers;
using YTDLHub.Bot.Services;
using YTDLHub.Bot.Workers;
using YTDLHub.Infrastructure.Data;
using YTDLHub.Infrastructure.Extensions;
using YTDLHub.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// ── Telegram Bot Client ───────────────────────────────────────────
var token = builder.Configuration["Telegram:BotToken"];
if (!string.IsNullOrWhiteSpace(token))
{
    var baseUrl = builder.Configuration["Telegram:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(new TelegramBotClientOptions(token, baseUrl)));
    }
    else
    {
        builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(token));
    }
}
else
{
    // Provide a dummy client for EF Core design-time
    builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient("123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11"));
}

// ── yt-dlp & Database Infrastructure ──────────────────────────────
builder.Services.AddYtDlpInfrastructure();
builder.Services.AddYtDlpDatabase(builder.Configuration);

// ── Bot Services ──────────────────────────────────────────────────
builder.Services.AddSingleton<UserStateService>();

// Handlers are scoped (one per Telegram update)
builder.Services.AddScoped<MessageHandler>();
builder.Services.AddScoped<CallbackHandler>();

// ── Background Worker ─────────────────────────────────────────────
if (!string.IsNullOrWhiteSpace(token))
{
    builder.Services.AddHostedService<BotWorker>();
}

var app = builder.Build();

// Auto-migrate Database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
