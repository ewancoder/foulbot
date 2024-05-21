using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace FoulBot.Api;

public sealed class TelegramUpdateHandlerFactory
{
    private readonly ILogger<TelegramUpdateHandler> _logger;
    private readonly ChatPool _chatPool;
    private readonly IFoulBotFactory _botFactory;

    public TelegramUpdateHandlerFactory(
        ILogger<TelegramUpdateHandler> logger,
        ChatPool chatPool,
        IFoulBotFactory botFactory)
    {
        _logger = logger;
        _chatPool = chatPool;
        _botFactory = botFactory;
    }

    public TelegramUpdateHandler Create(FoulBotConfiguration configuration)
    {
        return new TelegramUpdateHandler(
            _logger, _chatPool, _botFactory, configuration);
    }
}

public sealed class TelegramUpdateHandler : IUpdateHandler
{
    private readonly ILogger<TelegramUpdateHandler> _logger;
    private readonly ChatPool _chatPool;
    private readonly IFoulBotFactory _botFactory;
    private readonly FoulBotConfiguration _botConfiguration;
    private bool _coldStarted; // Make a delay on first startup so all the bots are properly initialized.

    public TelegramUpdateHandler(
        ILogger<TelegramUpdateHandler> logger,
        ChatPool chatPool,
        IFoulBotFactory botFactory,
        FoulBotConfiguration botConfiguration)
    {
        _logger = logger;
        _chatPool = chatPool;
        _botFactory = botFactory;
        _botConfiguration = botConfiguration;
    }

    public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Polling error from the bot {bot}", botClient.BotId);
        return Task.CompletedTask;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (!_coldStarted)
        {
            await Task.Delay(2000);
            _coldStarted = true;
        }

        _logger.LogDebug("Received update {@update} from bot {botId}", update, _botConfiguration.BotId);

        try
        {
            await _chatPool.HandleUpdateAsync(_botConfiguration.BotId, update, () => _botFactory.Create(botClient, _botConfiguration));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to handle update from bot {botId}.", _botConfiguration.BotId);
        }
    }
}
