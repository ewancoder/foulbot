using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FoulBot.Domain;
using FoulBot.Infrastructure;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace FoulBot.Api;

public sealed class TelegramUpdateHandler : IUpdateHandler
{
    private readonly ILogger<TelegramBotMessenger> _botMessengerLogger;
    private readonly ILogger<TelegramUpdateHandler> _logger;
    private readonly ChatPool _chatPool;
    private readonly IFoulBotFactory _botFactory;
    private readonly FoulBotConfiguration _botConfiguration;
    private readonly DateTime _coldStarted = DateTime.UtcNow + TimeSpan.FromSeconds(2); // Make a delay on first startup so all the bots are properly initialized.

    public TelegramUpdateHandler(
        ILogger<TelegramBotMessenger> botMessengerLogger, // TODO: Move to another factory.
        ILogger<TelegramUpdateHandler> logger,
        ChatPool chatPool,
        IFoulBotFactory botFactory,
        FoulBotConfiguration botConfiguration)
    {
        _botMessengerLogger = botMessengerLogger;
        _logger = logger;
        _chatPool = chatPool;
        _botFactory = botFactory;
        _botConfiguration = botConfiguration;

        using var _ = _logger.BeginScope(LogContext);
        _logger.LogInformation("Initialized TelegramUpdateHandler with configuration {@Configuration}.", _botConfiguration);
    }

    private Dictionary<string, object> LogContext => new()
    {
        ["BotId"] = _botConfiguration.BotId
    };

    public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        using var _ = _logger.BeginScope(LogContext);
        _logger.LogError(exception, "Polling error occurred.");
        return Task.CompletedTask;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        using var _ = _logger.BeginScope(LogContext);

        // Consider caching instances for every botClient instead of creating a lot of them.
        var botMessenger = new TelegramBotMessenger(_botMessengerLogger, botClient);

        if (DateTime.UtcNow < _coldStarted)
        {
            _logger.LogInformation("Handling update on cold start, delaying for 2 seconds.");
            await Task.Delay(2000);
        }

        _logger.LogDebug("Received update {@Update}.", update);

        try
        {
            await _chatPool.HandleUpdateAsync(_botConfiguration.BotId, update, chat => _botFactory.Create(botMessenger, _botConfiguration, chat));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to handle update {@Update}.", update);
        }
    }
}
