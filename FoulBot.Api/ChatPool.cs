using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace FoulBot.Api;

public sealed class ChatPool : IUpdateHandler
{
    private readonly List<Func<IFoulBot>> _enabledBots = new List<Func<IFoulBot>>();
    private readonly ConcurrentDictionary<long, FoulChat> _chats
        = new ConcurrentDictionary<long, FoulChat>();

    public void AddBot(Func<IFoulBot> botFactory)
    {
        _enabledBots.Add(botFactory);
    }

    public FoulChat? GetOrAddFoulChat(ChatId chatId)
    {
        return _chats.GetOrAdd(chatId.Identifier!.Value, CreateChat);
    }

    public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        // TODO: Handle error.
        return Task.CompletedTask;
    }

    public Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message == null || update.Message.Chat == null)
            return Task.CompletedTask;

        var chatId = update.Message.Chat.Id;

        var chat = _chats.GetOrAdd(chatId, CreateChat);

        chat.HandleUpdate(update);
        return Task.CompletedTask;
    }

    private FoulChat CreateChat(long chatId)
    {
        var chat = new FoulChat(chatId);

        foreach (var botFactory in _enabledBots)
        {
            // TODO: Only create bots that are present in chat. AND when bot is added to chat - add it too.
            var bot = botFactory();
            bot.JoinChat(chat);
        }

        return chat;
    }
}
