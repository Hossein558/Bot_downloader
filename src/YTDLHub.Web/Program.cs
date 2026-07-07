using Syncfusion.Blazor;
using Telegram.Bot;
using YTDLHub.Bot.Handlers;
using YTDLHub.Bot.Services;
using YTDLHub.Bot.Workers;
using YTDLHub.Infrastructure.Extensions;
using YTDLHub.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Register Syncfusion License
var syncfusionLicense = builder.Configuration["Syncfusion:LicenseKey"] 
    ?? "MTIzQDMzMzEyZTMwMmUzMDNiMzMzMTNiR0VoN2NVYVlJaHVIRHpqeTgxakxVVktQUmhUWkgvdzlUQVRtTW9XYXNmVT0=";
Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(syncfusionLicense);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSyncfusionBlazor();

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
    // If not provided in configuration, try environment variable or skip registering
    // This allows the Web App to run even if the Bot Token isn't provided (e.g., UI dev mode)
}

// ── yt-dlp Infrastructure ─────────────────────────────────────────
builder.Services.AddYtDlpInfrastructure();

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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
