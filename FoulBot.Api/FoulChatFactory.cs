﻿using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace FoulBot.Api;

public interface IFoulChatFactory
{
    IFoulChat Create(ChatId chatId, bool isPrivate);
}

public sealed class FoulChatFactory : IFoulChatFactory
{
    private readonly ILogger<FoulChat> _foulChatLogger;

    public FoulChatFactory(ILogger<FoulChat> foulChatLogger)
    {
        _foulChatLogger = foulChatLogger;
    }

    public IFoulChat Create(ChatId chatId, bool isPrivate)
    {
        return new FoulChat(_foulChatLogger, chatId, isPrivate);
    }
}
