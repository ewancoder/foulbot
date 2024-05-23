using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FoulBot.Api;

public sealed class ChatPool
{
    private readonly ILogger<ChatPool> _logger;
    private readonly ChatLoader _chatLoader;
    private readonly IFoulChatFactory _foulChatFactory;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private readonly HashSet<string> _joinedBots = new HashSet<string>();
    private readonly ConcurrentDictionary<string, IFoulChat> _chats
        = new ConcurrentDictionary<string, IFoulChat>();

    public ChatPool(
        ILogger<ChatPool> logger,
        ChatLoader chatLoader,
        IFoulChatFactory foulChatFactory)
    {
        _logger = logger;
        _chatLoader = chatLoader;
        _foulChatFactory = foulChatFactory;

        _logger.LogInformation("ChatPool instance is created.");
    }

    private IScopedLogger Logger => _logger.AddScoped();

    // This is being called from outside to join bots to chats after app restart.
    // And also from this class whenever new bot in a new chat receives a message and needs to be created.
    public async Task<IFoulChat> InitializeChatAndBotAsync(string botId, string chatId, Func<IFoulChat, IFoulBot> botFactory, string? invitedBy = null)
    {
        using var _ = Logger
            .AddScoped("BotId", botId)
            .AddScoped("ChatId", chatId)
            .AddScoped("InvitedBy", invitedBy)
            .BeginScope();

        if (!_chats.TryGetValue(chatId, out var chat))
        {
            _logger.LogInformation("Did not find the chat, creating it.");
            chat = await GetOrAddFoulChatAsync(chatId);
        }

        await JoinBotToChatIfNecessaryAsync(botId, chatId, chat, botFactory, invitedBy);

        return chat;
    }

    public async Task HandleUpdateAsync(string botId, Update update, Func<IFoulChat, IFoulBot> botFactory)
    {
        using var _ = Logger
            .AddScoped("BotId", botId)
            .AddScoped("ChatId", update?.MyChatMember?.Chat?.Id ?? update?.Message?.Chat?.Id)
            .BeginScope();

        if (update == null)
        {
            _logger.LogError("Received null update from Telegram.");
            return;
        }

        if (update.Type == UpdateType.MyChatMember)
        {
            _logger.LogDebug("Received MyChatMember update, initiating bot change status.");

            if (update.MyChatMember?.NewChatMember?.User?.Username == null
                || update.MyChatMember.Chat?.Id == null)
            {
                _logger.LogWarning("MyChatMember update doesn't have required properties. Skipping handling.");
                return;
            }

            var member = update.MyChatMember.NewChatMember;
            var chatId = update.MyChatMember.Chat.Id.ToString();
            var invitedByUsername = update.MyChatMember.From?.Username; // Who invited / kicked the bot.

            // TODO: The following 2 lines of code are duplicated in couple places.
            if (update.MyChatMember.Chat.Type == ChatType.Private)
                chatId += $"${botId}"; // Make separate chats for every bot, when talking to it in private. $ is a hack to split it later.

            var chat = await InitializeChatAndBotAsync(botId, chatId, botFactory, invitedByUsername);

            chat.ChangeBotStatus(
                member.User.Username,
                invitedByUsername,
                member.Status);

            _logger.LogInformation("Successfully handled message.");
            return;
        }

        if (update.Type == UpdateType.Message && update.Message?.Type == MessageType.Text)
        {
            _logger.LogDebug("Received Message update, handling the message.");

            if (update.Message?.Chat?.Id == null)
            {
                _logger.LogWarning("Message update doesn't have required properties. Skipping handling.");
                return;
            }

            var chatId = update.Message.Chat.Id.ToString();

            if (update.Message.Chat.Type == ChatType.Private)
                chatId += $"${botId}"; // Make separate chats for every bot, when talking to it in private. $ is a hack to split it later.

            // TODO: Here we can alter chatId to include botId in it if we need to, then the bot will have its own context separate from all the other bots.
            if (!_chats.TryGetValue(chatId, out var chat))
                chat = await GetOrAddFoulChatAsync(chatId);

            await JoinBotToChatIfNecessaryAsync(botId, chatId, chat, botFactory);

            chat.HandleTelegramMessage(update.Message);

            _logger.LogInformation("Successfully handled message.");
            return;
        }

        // TODO: Configure to only receive necessary types of updates.
        _logger.LogDebug("Received unnecessary update, skipping handling.");
    }

    private async ValueTask<IFoulChat> GetOrAddFoulChatAsync(string chatId)
    {
        _chats.TryGetValue(chatId, out var chat);
        if (chat != null)
            return chat;

        _logger.LogInformation("Chat is not created yet. Waiting for lock to start creating.");
        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation("Entered lock for creating the chat.");
            _chats.TryGetValue(chatId, out chat);
            if (chat != null)
            {
                _logger.LogInformation("Some other thread has created the chat, skipping.");
                return chat;
            }

            _logger.LogInformation("Creating the chat and caching it for future.");
            var longChatId = chatId.Contains("$")
                ? Convert.ToInt64(chatId.Split("$")[0])
                : Convert.ToInt64(chatId);
            chat = _foulChatFactory.Create(new ChatId(longChatId), chatId.Contains("$"));
            chat = _chats.GetOrAdd(chatId, chat);

            if (chatId.Contains("$"))
                _logger.LogInformation("Created a PRIVATE chat.");

            _chatLoader.AddChat(chatId);
            _logger.LogInformation("Successfully created the chat.");

            return chat;
        }
        finally
        {
            _logger.LogInformation("Releasing lock for creating chat.");
            _lock.Release();
        }
    }

    private async ValueTask JoinBotToChatIfNecessaryAsync(string botId, string chatId, IFoulChat chat, Func<IFoulChat, IFoulBot> botFactory, string? invitedBy = null)
    {
        if (_joinedBots.Contains($"{botId}{chatId}"))
            return;

        using var _ = Logger
            .AddScoped("BotId", botId)
            .AddScoped("ChatId", chatId)
            .AddScoped("InvitedBy", invitedBy)
            .BeginScope();

        _logger.LogInformation("Did not find the bot, creating and joining it to the chat. Waiting to acquire lock.");
        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation("Entered lock for creating and joining the bot.");
            if (_joinedBots.Contains($"{botId}{chatId}"))
            {
                _logger.LogInformation("Another thread has added this bot, skipping.");
                return;
            }

            _logger.LogInformation("Creating the bot and joining it to chat.");
            var bot = botFactory(chat);
            await bot.JoinChatAsync(invitedBy); // TODO: Consider refactoring this to inside of botFactory or FoulBot constructor altogether.
            _joinedBots.Add($"{botId}{chatId}");
            _logger.LogInformation("The bot is successfully added to chat.");
        }
        finally
        {
            _logger.LogInformation("Releasing lock");
            _lock.Release();
        }
    }
}
