using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace FoulBot.Api;

// TODO: Improve logs here to include specific bot information.
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
        _logger = logger;
        _logger.LogInformation("ChatPool instance is created. Application has started. Start time is {AppStartedTime}", _appStarted);
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

        _logger.LogInformation("Chat {chatId} is not created yet, creating using a lock. Invited by {invitedBy}", chatId, invitedBy);

        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation("Entered lock for creating {chatId} by {invitedBy}", chatId, invitedBy);
            _chats.TryGetValue(chatId, out chat);
            if (chat != null)
                return chat;

            chat = await CreateChatAsync(chatId, invitedBy);
            chat = _chats.GetOrAdd(chatId, chat);

            return chat;
        }
        finally
        {
            _logger.LogInformation("Releasing lock for creating {chatId} by {invitedBy}", chatId, invitedBy);
            _lock.Release();
        }
    }

    public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Polling error from the bot {bot}", botClient.BotId);
        return Task.CompletedTask;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Received update {@update}", update);

        if (update.Message?.Chat == null
            && update.MyChatMember?.Chat == null)
        {
            return;
        }

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

        _logger.LogInformation("Created chat {chatId}, invited by {invitedBy}. Adding all the bots to it now.", chatId, invitedBy);
        foreach (var botFactory in _enabledBots)
        {
            var bot = botFactory();
            _logger.LogInformation("Adding bot {botId} to the chat {chatId}", bot.BotId);
            await bot.JoinChatAsync(chat, invitedBy);
            _logger.LogInformation("Added bot {botId} to the chat {chatId}", bot.BotId);
        }

        return chat;
    }
}
