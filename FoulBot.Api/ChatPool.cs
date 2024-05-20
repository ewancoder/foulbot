using Microsoft.Extensions.Logging;
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

public sealed class TelegramUpdateHandlerFactory
{
    private readonly ILogger<TelegramUpdateHandler> _logger;
    private readonly ChatPool _chatPool;

    public TelegramUpdateHandlerFactory(
        ILogger<TelegramUpdateHandler> logger,
        ChatPool chatPool)
    {
        _logger = logger;
        _chatPool = chatPool;
    }

    public TelegramUpdateHandler CreateHandler(string botId, Func<IFoulBot> botFactory)
    {
        return new TelegramUpdateHandler(_chatPool, botId, botFactory, _logger);
    }
}

public sealed class TelegramUpdateHandler : IUpdateHandler
{
    private readonly ChatPool _chatPool;
    private readonly string _botId;
    private readonly Func<IFoulBot> _botFactory;
    private readonly ILogger<TelegramUpdateHandler> _logger;

    public TelegramUpdateHandler(
        ChatPool chatPool,
        string botId,
        Func<IFoulBot> botFactory,
        ILogger<TelegramUpdateHandler> logger)
    {
        _chatPool = chatPool;
        _botId = botId;
        _botFactory = botFactory;
        _logger = logger;
    }

    public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Polling error from the bot {bot}", botClient.BotId);
        return Task.CompletedTask;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Received update {@update} from bot {botId}", update, _botId);

        try
        {
            await _chatPool.HandleUpdateAsync(_botId, update, _botFactory);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to handle update from bot {botId}.", _botId);
        }
    }
}

public sealed class ChatPool
{
    private readonly ILogger<ChatPool> _logger;
    private readonly ILogger<FoulChat> _foulChatLogger;
    private readonly DateTime _appStarted = DateTime.UtcNow;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private readonly HashSet<string> _joinedBots = new HashSet<string>();
    private readonly ConcurrentDictionary<long, FoulChat> _chats
        = new ConcurrentDictionary<long, FoulChat>();

    public ChatPool(
        ILogger<ChatPool> logger,
        ILogger<FoulChat> foulChatLogger)
    {
        _logger = logger;
        _foulChatLogger = foulChatLogger;
        _logger.LogInformation("ChatPool instance is created. Application has started. Start time is {AppStartedTime}", _appStarted);
    }

    public async Task HandleUpdateAsync(string botId, Update update, Func<IFoulBot> botFactory)
    {
        if (update.Type == UpdateType.MyChatMember)
        {
            _logger.LogDebug("Bot {botId} Received MyChatMember update, initiating bot change status.", botId);
            var member = update.MyChatMember.NewChatMember;
            var chatId = update.MyChatMember.Chat.Id;
            var invitedByUsername = update.MyChatMember.From?.Username; // Who invited / kicked the bot.

            if (!_chats.TryGetValue(chatId, out var chat))
                chat = await GetOrAddFoulChatAsync(chatId, invitedByUsername, botId, botFactory);

            await JoinBotToChatIfNecessaryAsync(botId, chatId, chat, invitedByUsername, botFactory);

            await chat.ChangeBotStatusAsync(
                member.User.Username,
                invitedByUsername,
                member.Status);

            return;
        }

        // Added or left - can comment on it.
        //if (update.Type == UpdateType.Message && update.Message.Type == MessageType.ChatMembersAdded)

        if (update.Type == UpdateType.Message && update.Message.Type == MessageType.Text)
        {
            _logger.LogDebug("Bot {botId} Received Message update, initiating message handling.", botId);
            var chatId = update.Message.Chat.Id;

            if (!_chats.TryGetValue(chatId, out var chat))
                chat = await GetOrAddFoulChatAsync(chatId, null, botId, botFactory);

            await JoinBotToChatIfNecessaryAsync(botId, chatId, chat, null, botFactory);

            chat.HandleUpdate(update);

            return;
        }

        // TODO: Configure to only receive necessary types of updates.
        _logger.LogDebug("Received unnecessary update from the bot {botId}, skipping handling.", botId);
    }

    public async ValueTask<FoulChat> GetOrAddFoulChatAsync(long chatId, string? invitedBy, string botId, Func<IFoulBot> botFactory)
    {
        _chats.TryGetValue(chatId, out var chat);
        if (chat != null)
            return chat;

        _logger.LogInformation("Chat {chatId} is not created for bot {botId} yet, creating using a lock. Invited by {invitedBy}", chatId, botId, invitedBy);

        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation("Entered lock for creating {chatId} for {botId} by {invitedBy}", chatId, botId, invitedBy);
            _chats.TryGetValue(chatId, out chat);
            if (chat != null)
                return chat;

            chat = new FoulChat(_foulChatLogger, chatId, _appStarted);
            chat = _chats.GetOrAdd(chatId, chat);

            return chat;
        }
        finally
        {
            _logger.LogInformation("Releasing lock for creating {chatId} for {botId} by {invitedBy}", chatId, botId, invitedBy);
            _lock.Release();
        }
    }

    private async ValueTask JoinBotToChatIfNecessaryAsync(string botId, long chatId, FoulChat chat, string invitedByUsername, Func<IFoulBot> botFactory)
    {
        if (_joinedBots.Contains($"{botId}{chatId}"))
            return;

        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation("Entered lock for joining {botId} bot to {chatId} chat.", botId, chatId);
            if (!_joinedBots.Contains($"{botId}{chatId}"))
            {
                _logger.LogInformation("Bot {botId} is not created for chat {chatId}, invited by {invitedBy}. Creating now.", botId, chatId, invitedByUsername);
                var bot = botFactory();
                await bot.JoinChatAsync(chat, invitedByUsername);
                _joinedBots.Add($"{botId}{chatId}");
                _logger.LogInformation("Bot {botId} is successfully added to chat {chatId}. Invited by {invitedBy}.", botId, chatId, invitedByUsername);
            }
        }
        finally
        {
            _logger.LogInformation("Releasing lock for joining {botId} bot to {chatId} chat.", botId, chatId);
            _lock.Release();
        }
    }
}
