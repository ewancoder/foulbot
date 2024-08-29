using System.Collections.Concurrent;
using FoulBot.Domain.Storage;

namespace FoulBot.Domain;

public sealed class ChatPool : IAsyncDisposable
{
    private readonly ILogger<ChatPool> _logger;
    private readonly IFoulChatFactory _chatFactory;
    // TODO: Consider using this factory instead of a delegate.
    //private readonly IFoulBotFactory _botFactory;
    private readonly IDuplicateMessageHandler _duplicateMessageHandler;
    private readonly IAllowedChatsProvider _allowedChatsProvider;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, IFoulBot> _bots = []; // Key is {BotId}{ChatId}
    private readonly ConcurrentDictionary<string, IFoulChat> _chats = new(); // Key is {ChatId}${BotId}
    private bool _isStopping;

    public ChatPool(
        ILogger<ChatPool> logger,
        IFoulChatFactory foulChatFactory,
        IDuplicateMessageHandler duplicateMessageHandler,
        IAllowedChatsProvider allowedChatsProvider)
    {
        _logger = logger;
        _chatFactory = foulChatFactory;
        _duplicateMessageHandler = duplicateMessageHandler;
        _allowedChatsProvider = allowedChatsProvider;

        _logger.LogInformation("ChatPool instance is created with {DuplicateMessageHandler}", _duplicateMessageHandler.GetType());
    }

    private IScopedLogger Logger => _logger.AddScoped();

    /// <summary>
    /// Used by chatloader to initially load all the known chats on app startup.
    /// And also internally by HandleMessageAsync method.
    /// Returns null when chat is not allowed.
    /// </summary>
    public async Task<IFoulChat> InitializeChatAndBotAsync(
        FoulChatId chatId,
        FoulBotId botId,
        JoinBotToChatAsync botFactory,
        string? invitedBy,
        CancellationToken cancellationToken)
    {
        using var _ = Logger
            .AddScoped("ChatId", chatId)
            .AddScoped("BotId", botId)
            .AddScoped("InvitedBy", invitedBy)
            .BeginScope();

        // Do not log this as warning! It happens every time a message is received.
        _logger.LogTrace("Acquiring chat and bot");

        // TODO: Here we can alter chatId to include botId in it if we need to,
        // then the bot will have its own context separate from all the other bots.

        var chat = await GetOrAddFoulChatAsync(chatId, cancellationToken);

        try
        {
            await JoinBotToChatIfNecessaryAsync(
                botId, chat, botFactory, invitedBy, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error when trying to add bot to chat");
            throw;
        }

        return chat;
    }

    public async ValueTask InviteBotToChatAsync(
        FoulChatId chatId,
        FoulBotId foulBotId,
        string? invitedBy,
        JoinBotToChatAsync botFactory,
        CancellationToken cancellationToken)
    {
        using var _l = Logger
            .AddScoped("ChatId", chatId)
            .AddScoped("BotId", foulBotId)
            .AddScoped("InvitedBy", invitedBy)
            .BeginScope();

        _logger.LogWarning("Bot has been invited to a new chat by {InvitedBy}", invitedBy);

        if (!await IsAllowedChatAsync(chatId))
            return;

        // TODO: Consider throwing exception here. If bot wasn't able to join the chat - it just returns.
        var chat = await InitializeChatAndBotAsync(
            chatId,
            foulBotId,
            botFactory,
            invitedBy: invitedBy,
            cancellationToken: cancellationToken);
    }

    public async ValueTask KickBotFromChatAsync(
        FoulChatId chatId,
        FoulBotId foulBotId,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        using var _l = Logger
            .AddScoped("ChatId", chatId)
            .AddScoped("BotId", foulBotId)
            .BeginScope();

        if (!_bots.TryGetValue(GetKeyForBot(foulBotId, chatId), out var bot))
        {
            _logger.LogWarning("Could not kick bot from chat. It already doesn't exist.");
            return;
        }

        await bot!.GracefulShutdownAsync();

        _logger.LogWarning("Kicked bot from chat.");
    }

    public async ValueTask HandleMessageAsync(
        FoulChatId chatId,
        FoulBotId foulBotId,
        FoulMessage message,
        JoinBotToChatAsync botFactory,
        CancellationToken cancellationToken)
    {
        using var _ = Logger
            .AddScoped("ChatId", chatId)
            .AddScoped("BotId", foulBotId)
            .AddScoped("MessageId", message.Id)
            .BeginScope();

        _logger.LogDebug("Received {@Message}, handling the message", message);

        if (!await IsAllowedChatAsync(chatId))
        {
            // Just for convenience of reading logs from not allowed chats.
            _logger.LogTrace("Received {Message} from not allowed chat {Chat} by bot {Bot}", message, chatId, foulBotId);
            return;
        }

        var chat = await InitializeChatAndBotAsync(
            chatId,
            foulBotId,
            botFactory,
            invitedBy: null,
            cancellationToken: cancellationToken);

        await chat.HandleMessageAsync(message);

        _logger.LogInformation("Successfully handled the message by Chat. Now it's Bots turn");
    }

    private async ValueTask<IFoulChat> GetOrAddFoulChatAsync(
        FoulChatId chatId, CancellationToken cancellationToken)
    {
        var key = GetKeyForChat(chatId);

        _chats.TryGetValue(key, out var chat);
        if (chat != null)
            return chat;

        _logger.LogDebug("Chat is not created yet. Waiting for lock to start creating");
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogTrace("Entered lock for creating the chat");
            _chats.TryGetValue(key, out chat);
            if (chat != null)
            {
                _logger.LogTrace("Some other thread has created the chat, skipping");
                return chat;
            }

            _logger.LogInformation("Creating the chat and caching it for future");
            chat = await _chatFactory.CreateAsync(_duplicateMessageHandler, chatId);
            chat = _chats.GetOrAdd(key, chat);

            if (chatId.IsPrivate)
                _logger.LogInformation("Created a PRIVATE chat");

            _logger.LogInformation("Successfully created the chat");

            return chat;
        }
        finally
        {
            _logger.LogTrace("Releasing lock for creating the chat");
            _lock.Release();
        }
    }

    private async ValueTask JoinBotToChatIfNecessaryAsync(
        FoulBotId foulBotId,
        IFoulChat chat,
        JoinBotToChatAsync botFactory,
        string? invitedBy,
        CancellationToken cancellationToken)
    {
        var key = GetKeyForBot(foulBotId, chat.ChatId);

        if (_bots.ContainsKey(key))
            return;

        _logger.LogDebug("Did not find the bot, creating and joining it to the chat. Waiting to acquire lock");
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogTrace("Entered lock for creating and joining the bot");
            if (_bots.ContainsKey(key))
            {
                _logger.LogTrace("Another thread has added this bot, skipping");
                return;
            }

            _logger.LogInformation("Creating the bot and joining it to chat");
            var bot = await botFactory(chat);
            if (bot == null)
            {
                // TODO: Debounce this: when bot cannot join - short circuit all parallel requests.
                // Make sure other tasks waiting on a lock won't go and try to create it again,
                // for example for 5 seconds. It will prevent initial 'ddos' attack.
                _logger.LogInformation("Could not add this bot to this chat");
                return;
            }

            void Trigger(object? s, FoulMessage message)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await bot.TriggerAsync(message);
                    }
                    catch (Exception exception)
                    {
                        // TODO: Unit test that we don't have unhandled exceptions anymore.
                        using var _l = Logger
                            .AddScoped("ChatId", chat.ChatId)
                            .AddScoped("BotId", foulBotId)
                            .AddScoped("Message", message)
                            .BeginScope();
                        _logger.LogError(exception, "Error when processing a bot trigger.");
                    }
                }, cancellationToken);
            }

            // Consider rewriting to System.Reactive.
            chat.MessageReceived += Trigger;
            _logger.LogDebug("Subscribed this bot to message received events from chat");

            // When we fail to send a message to chat, cleanup the bot (it will be recreated after more messages received).
            bot.Shutdown += async (sender, e) =>
            {
                using var _l = Logger
                    .AddScoped("ChatId", chat.ChatId)
                    .AddScoped("BotId", foulBotId)
                    .AddScoped("InvitedBy", invitedBy)
                    .BeginScope();

                _logger.LogWarning("Bot shutdown event has been received. Unsubscribing and disposing");

                chat.MessageReceived -= Trigger;

                _bots.Remove(key, out _);

                await bot.DisposeAsync();

                _logger.LogWarning("Successfully unsubscribed and disposed of bot");
            };
            _logger.LogDebug("Subscribed to bot shutdown event, so we can dispose of bot when needed");

            if (invitedBy != null)
            {
                _logger.LogDebug("Greeting people in chat who invited the bot");
                await bot.GreetEveryoneAsync(new(invitedBy));
            }

            if (!_bots.TryAdd($"{foulBotId.BotId}{chat.ChatId.Value}", bot))
            {
                _logger.LogError("This should never happen. Bot was not added to the dictionary");
                await bot.GracefulShutdownAsync();
                throw new InvalidOperationException("Could not add bot to chat.");
            }

            _logger.LogInformation("Adding bot to chat operation was performed");
        }
        finally
        {
            _logger.LogTrace("Releasing lock for adding bot to chat");
            _lock.Release();
        }
    }

    public async Task GracefullyCloseAsync()
    {
        _logger.LogDebug("Graceful shutdown of ChatPool has been initiated. Disposing of everything");

        if (_isStopping)
        {
            _logger.LogDebug("It is already stopping. Skipping performing Graceful shutdown again");
            return;
        }

        _isStopping = true;

        await Task.WhenAll(
            _chats.Values.ToList().Select(chat => chat.GracefullyCloseAsync()).Concat(
                _bots.Values.ToList().Select(bot => bot.GracefulShutdownAsync())));

        _logger.LogDebug("Graceful shutdown of ChatPool successfully finished");
    }

    public async ValueTask DisposeAsync()
    {
        if (!_isStopping)
            await GracefullyCloseAsync();

        _lock.Dispose();
    }

    private static string GetKeyForBot(FoulBotId botId, FoulChatId chatId)
        => $"{botId.BotId}{chatId.Value}";

    private static string GetKeyForChat(FoulChatId chatId) => chatId.IsPrivate
        ? $"{chatId.Value}${chatId.FoulBotId?.BotId}"
        : $"{chatId.Value}";

    private async ValueTask<bool> IsAllowedChatAsync(FoulChatId chatId)
    {
        var isAllowed = await _allowedChatsProvider.IsAllowedChatAsync(chatId);
        if (!isAllowed)
            _logger.LogWarning("Chat is not allowed");

        return isAllowed;
    }
}
