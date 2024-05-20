using Google.Apis.Logging;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FoulBot.Api;

public sealed class ChatPool : IUpdateHandler
{
    private readonly ILogger<ChatPool> _logger;
    private readonly DateTime _appStarted = DateTime.UtcNow;
    private readonly List<Func<IFoulBot>> _enabledBots = new List<Func<IFoulBot>>();
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private readonly ConcurrentDictionary<long, FoulChat> _chats
        = new ConcurrentDictionary<long, FoulChat>();

    public ChatPool(ILogger<ChatPool> logger)
    {
        Log.Warning("test {abc}", "hah");
        _logger = logger;
        _logger.LogDebug("ChatPool instance is created. Application has started. Start time is {AppStartedTime}", _appStarted);
    }

    public void AddBot(Func<IFoulBot> botFactory)
    {
        _enabledBots.Add(botFactory);
    }

    public async ValueTask<FoulChat> GetOrAddFoulChatAsync(long chatId, string? invitedBy)
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

            chat = await CreateChatAsync(chatId, invitedBy);
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
        if (update.Message?.Chat == null
            && update.MyChatMember?.Chat == null)
            return;

        var member = update.MyChatMember?.NewChatMember;

        var chatId = member == null
            ? update.Message.Chat.Id
            : update.MyChatMember.Chat.Id;

        if (!_chats.TryGetValue(chatId, out var chat))
            chat = await GetOrAddFoulChatAsync(chatId, update.MyChatMember?.From?.Username);

        if (member != null)
        {
            await chat.ChangeBotStatusAsync(member.User.Username, update.MyChatMember.From.Username, member.Status);
        }
        else
        {
            chat.HandleUpdate(update);
        }
    }

    private async ValueTask<FoulChat> CreateChatAsync(long chatId, string? invitedBy)
    {
        var chat = new FoulChat(chatId, _appStarted);

        foreach (var botFactory in _enabledBots)
        {
            // TODO: Only create bots that are present in chat. AND when bot is added to chat - add it too.
            var bot = botFactory();
            await bot.JoinChatAsync(chat, invitedBy);
        }

        return chat;
    }
}
