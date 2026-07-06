using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using YTDLHub.Bot.Handlers;

namespace YTDLHub.Bot.Workers;

/// <summary>
/// Long-running background service that starts the Telegram update polling loop.
/// </summary>
public sealed class BotWorker : BackgroundService, IUpdateHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BotWorker> _logger;

    public BotWorker(
        ITelegramBotClient bot,
        IServiceScopeFactory scopeFactory,
        ILogger<BotWorker> logger)
    {
        _bot          = bot;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var me = await _bot.GetMe(stoppingToken);
        _logger.LogInformation("Bot started as @{Username} (id={Id})", me.Username, me.Id);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates =
            [
                UpdateType.Message,
                UpdateType.CallbackQuery
            ],
            DropPendingUpdates = true   // ignore messages that arrived while bot was offline
        };

        // Telegram.Bot v22+: ReceiveAsync accepts an IUpdateHandler implementation
        await _bot.ReceiveAsync(
            updateHandler:   this,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);
    }

    // ── IUpdateHandler ────────────────────────────────────────────────────────

    public async Task HandleUpdateAsync(
        ITelegramBotClient bot,
        Update update,
        CancellationToken ct)
    {
        // Each update is handled in its own DI scope
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        try
        {
            if (update.Message is { Text: not null } message)
            {
                if (message.Text.StartsWith("/start") || message.Text.StartsWith("/help"))
                {
                    await StartHandler.HandleAsync(bot, message, ct);
                }
                else
                {
                    var handler = sp.GetRequiredService<MessageHandler>();
                    await handler.HandleAsync(bot, message, ct);
                }
            }
            else if (update.CallbackQuery is { } callback)
            {
                var handler = sp.GetRequiredService<CallbackHandler>();
                await handler.HandleAsync(bot, callback, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error while processing update {UpdateId}", update.Id);
        }
    }

    public Task HandleErrorAsync(
        ITelegramBotClient bot,
        Exception ex,
        HandleErrorSource source,
        CancellationToken ct)
    {
        _logger.LogError(ex, "Telegram polling error [{Source}]", source);
        return Task.CompletedTask;
    }
}
