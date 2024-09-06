﻿using FoulBot.Domain.Connections;
using Telegram.Bot;

namespace FoulBot.Infrastructure.Telegram;

public sealed class TelegramBotConnectionHandler : IBotConnectionHandler
{
    private readonly ITelegramUpdateHandlerFactory _telegramUpdateHandlerFactory;
    private readonly ITelegramBotMessengerFactory _botMessengerFactory;

    public TelegramBotConnectionHandler(
        ITelegramUpdateHandlerFactory telegramUpdateHandlerFactory,
        ITelegramBotMessengerFactory botMessengerFactory)
    {
        _telegramUpdateHandlerFactory = telegramUpdateHandlerFactory;
        _botMessengerFactory = botMessengerFactory;
    }

    public ValueTask<IBotMessenger> StartHandlingAsync(BotConnectionConfiguration configuration, CancellationToken cancellationToken)
    {
        var client = new TelegramBotClient(configuration.ConnectionString);

        var botMessenger = _botMessengerFactory.Create(client);

        client.StartReceiving(
            _telegramUpdateHandlerFactory.Create(configuration.Configuration, client, botMessenger),
            cancellationToken: cancellationToken);

        return new(botMessenger);
    }
}
