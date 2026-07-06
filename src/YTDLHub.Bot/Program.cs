using Telegram.Bot;
using YTDLHub.Bot.Handlers;
using YTDLHub.Bot.Services;
using YTDLHub.Bot.Workers;
using YTDLHub.Infrastructure.Extensions;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        // ── Telegram Bot Client ───────────────────────────────────────────
        var token = config["Telegram:BotToken"]
            ?? throw new InvalidOperationException(
                "کلید 'Telegram:BotToken' در appsettings.json یا environment variables تنظیم نشده.");

        var baseUrl = config["Telegram:BaseUrl"];

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(new TelegramBotClientOptions(token, baseUrl)));
        }
        else
        {
            services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(token));
        }

        // ── yt-dlp Infrastructure ─────────────────────────────────────────
        services.AddYtDlpInfrastructure();

        // ── Bot Services ──────────────────────────────────────────────────
        services.AddSingleton<UserStateService>();

        // Handlers are scoped (one per Telegram update)
        services.AddScoped<MessageHandler>();
        services.AddScoped<CallbackHandler>();

        // ── Background Worker ─────────────────────────────────────────────
        services.AddHostedService<BotWorker>();
    })
    .Build();

await host.RunAsync();
