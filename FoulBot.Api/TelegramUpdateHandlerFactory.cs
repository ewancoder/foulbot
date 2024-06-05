using System.Collections.Generic;
using System.Linq;
using FoulBot.Domain;
using FoulBot.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Polling;

namespace FoulBot.Api;

public interface ITelegramUpdateHandlerFactory
{
    IUpdateHandler Create(FoulBotConfiguration configuration);
}

public sealed class TelegramUpdateHandlerFactory : ITelegramUpdateHandlerFactory
{
    private readonly ILogger<TelegramBotMessenger> _bmLogger;
    private readonly ILogger<TelegramUpdateHandler> _logger;
    private readonly ChatPool _chatPool;
    private readonly IFoulBotFactory _botFactory;
    private readonly IFoulMessageFactory _foulMessageFactory;
    private readonly IEnumerable<string> _allowedChats;

    public TelegramUpdateHandlerFactory(
        ILogger<TelegramBotMessenger> bmLogger,
        ILogger<TelegramUpdateHandler> logger,
        ChatPool chatPool,
        IFoulBotFactory botFactory,
        IFoulMessageFactory foulMessageFactory,
        IConfiguration configuration)
    {
        _bmLogger = bmLogger;
        _logger = logger;
        _chatPool = chatPool;
        _botFactory = botFactory;
        _foulMessageFactory = foulMessageFactory;

        var chats = configuration["AllowedChats"];
        _allowedChats = chats?.Split(',') ?? Enumerable.Empty<string>();
    }

    public IUpdateHandler Create(FoulBotConfiguration configuration)
    {
        return new TelegramUpdateHandler(
            _bmLogger, _logger, _chatPool, _botFactory, _foulMessageFactory, configuration, _allowedChats);
    }
}
