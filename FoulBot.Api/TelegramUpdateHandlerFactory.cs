using Microsoft.Extensions.Logging;
using Telegram.Bot.Polling;

namespace FoulBot.Api;

public interface ITelegramUpdateHandlerFactory
{
    IUpdateHandler Create(FoulBotConfiguration configuration);
}

public sealed class TelegramUpdateHandlerFactory : ITelegramUpdateHandlerFactory
{
    private readonly ILogger<BotMessenger> _bmLogger;
    private readonly ILogger<TelegramUpdateHandler> _logger;
    private readonly ChatPool _chatPool;
    private readonly IFoulBotFactory _botFactory;

    public TelegramUpdateHandlerFactory(
        ILogger<BotMessenger> bmLogger,
        ILogger<TelegramUpdateHandler> logger,
        ChatPool chatPool,
        IFoulBotFactory botFactory)
    {
        _bmLogger = bmLogger;
        _logger = logger;
        _chatPool = chatPool;
        _botFactory = botFactory;
    }

    public IUpdateHandler Create(FoulBotConfiguration configuration)
    {
        return new TelegramUpdateHandler(
            _bmLogger, _logger, _chatPool, _botFactory, configuration);
    }
}
