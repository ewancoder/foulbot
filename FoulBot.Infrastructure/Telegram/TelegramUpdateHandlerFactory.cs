using FoulBot.Domain.Storage;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Polling;

namespace FoulBot.Infrastructure.Telegram;

public interface ITelegramUpdateHandlerFactory
{
    IUpdateHandler Create(FoulBotConfiguration configuration, TelegramBotClient client);
}

public sealed class TelegramUpdateHandlerFactory : ITelegramUpdateHandlerFactory
{
    private readonly ILogger<TelegramBotMessenger> _bmLogger;
    private readonly ILogger<TelegramUpdateHandler> _logger;
    private readonly ChatPool _chatPool;
    private readonly IFoulBotFactory _botFactory;
    private readonly IFoulMessageFactory _foulMessageFactory;
    private readonly IAllowedChatsProvider _allowedChatsProvider;

    public TelegramUpdateHandlerFactory(
        ILogger<TelegramBotMessenger> bmLogger,
        ILogger<TelegramUpdateHandler> logger,
        [FromKeyedServices("Telegram")]ChatPool chatPool,
        IFoulBotFactory botFactory,
        IFoulMessageFactory foulMessageFactory,
        IAllowedChatsProvider allowedChatsProvider)
    {
        _bmLogger = bmLogger;
        _logger = logger;
        _chatPool = chatPool;
        _botFactory = botFactory;
        _foulMessageFactory = foulMessageFactory;
        _allowedChatsProvider = allowedChatsProvider;
    }

    public IUpdateHandler Create(FoulBotConfiguration configuration, TelegramBotClient client)
    {
        return new TelegramUpdateHandler(
            _bmLogger, _logger, _chatPool, _botFactory, _foulMessageFactory, configuration, _allowedChatsProvider, client);
    }
}
