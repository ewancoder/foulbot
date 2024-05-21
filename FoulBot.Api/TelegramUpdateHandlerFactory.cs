using Microsoft.Extensions.Logging;
using Telegram.Bot.Polling;

namespace FoulBot.Api;

public interface ITelegramUpdateHandlerFactory
{
    IUpdateHandler Create(FoulBotConfiguration configuration);
}

public sealed class TelegramUpdateHandlerFactory : ITelegramUpdateHandlerFactory
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

    public IUpdateHandler Create(FoulBotConfiguration configuration)
    {
        return new TelegramUpdateHandler(
            _logger, _chatPool, _botFactory, configuration);
    }
}
