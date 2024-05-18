﻿using System;
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
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private readonly ConcurrentDictionary<long, FoulChat> _chats
        = new ConcurrentDictionary<long, FoulChat>();

    public void AddBot(Func<IFoulBot> botFactory)
    {
        _enabledBots.Add(botFactory);
    }

    public async ValueTask<FoulChat> GetOrAddFoulChatAsync(long chatId)
    {
        _chats.TryGetValue(chatId, out var chat);
        if (chat != null)
            return chat;

        await _lock.WaitAsync();
        try
        {
            _chats.TryGetValue(chatId, out chat);
            if (chat != null)
                return chat;

            chat = await CreateChatAsync(chatId);
            _chats.GetOrAdd(chatId, chat);

            return chat;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        // TODO: Handle error.
        return Task.CompletedTask;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message == null || update.Message.Chat == null)
            return;

        var chatId = update.Message.Chat.Id;

        if (!_chats.TryGetValue(chatId, out var chat))
            chat = await GetOrAddFoulChatAsync(chatId);

        chat.HandleUpdate(update);
    }

    private async ValueTask<FoulChat> CreateChatAsync(long chatId)
    {
        var chat = new FoulChat(chatId);

        foreach (var botFactory in _enabledBots)
        {
            // TODO: Only create bots that are present in chat. AND when bot is added to chat - add it too.
            var bot = botFactory();
            await bot.JoinChatAsync(chat);
        }

        return chat;
    }
}
