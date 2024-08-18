using System.Collections.Concurrent;

namespace FoulBot.Domain;

public sealed class ChatPool : IAsyncDisposable
{
    private readonly ILogger<ChatPool> _logger;
    private readonly IChatCache _chatCache;
    private readonly IFoulChatFactory _foulChatFactory;
    private readonly IFoulBotNgFactory _foulBotFactory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly HashSet<string> _joinedBots = [];
    private readonly Dictionary<string, FoulBotNg> _joinedBotsObjects = [];
    private readonly ConcurrentDictionary<string, IFoulChat> _chats = new();
    private bool _isStopping;

    public ChatPool(
        ILogger<ChatPool> logger,
        IChatCache chatCache,
        IFoulChatFactory foulChatFactory,
        IFoulBotNgFactory foulBotFactory)
    {
        _logger = logger;
        _chatCache = chatCache;
        _foulChatFactory = foulChatFactory;
        _foulBotFactory = foulBotFactory;

        _logger.LogInformation("ChatPool instance is created.");
    }

    public IEnumerable<FoulBotNg> AllBots => _joinedBotsObjects.Values;

    private IScopedLogger Logger => _logger.AddScoped();

    public async Task<IFoulChat> InitializeChatAndBotAsync(string botId, string chatId, FoulChatToBotFactory botFactory, string? invitedBy = null, CancellationToken cancellationToken = default)
    {
        using var _ = Logger
            .AddScoped("BotId", botId)
            .AddScoped("ChatId", chatId)
            .AddScoped("InvitedBy", invitedBy)
            .BeginScope();

        // TODO: Here we can alter chatId to include botId in it if we need to, then the bot will have its own context separate from all the other bots.
        if (!_chats.TryGetValue(chatId, out var chat))
        {
            _logger.LogInformation("Did not find the chat, creating it.");
            chat = await GetOrAddFoulChatAsync(chatId, cancellationToken);
        }

        try
        {
            await JoinBotToChatIfNecessaryAsync(botId, chatId, chat, botFactory, invitedBy, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error when trying to add bot to chat.");
            throw;
        }

        return chat;
    }

    public async ValueTask UpdateStatusAsync(
        string chatId,
        string botId,
        string botUsername,
        BotChatStatus status,
        string? invitedByUsername,
        bool isPrivate,
        FoulChatToBotFactory botFactory,
        CancellationToken cancellationToken)
    {
        using var _ = Logger
            .AddScoped("BotId", botId)
            .AddScoped("ChatId", chatId)
            .BeginScope();

        _logger.LogDebug("Updating bot status: {BotUsername}, {Status}.", botUsername, status);

        if (isPrivate)
        {
            _logger.LogDebug("Chat is private, adding bot ID to chat ID so that its separate from other bots.");
            chatId += $"${botId}"; // Make separate chats for every bot, when talking to it in private. $ is a hack to split it later.
        }

        var chat = await InitializeChatAndBotAsync(botId, chatId, botFactory, invitedByUsername, cancellationToken);

        chat.ChangeBotStatus(
            botUsername,
            invitedByUsername,
            status);

        _logger.LogInformation("Successfully initiated bot change status.");
    }

    public async ValueTask HandleMessageAsync(
        string chatId,
        string botId,
        FoulMessage message,
        bool isPrivate,
        FoulChatToBotFactory botFactory,
        CancellationToken cancellationToken)
    {
        using var _ = Logger
            .AddScoped("BotId", botId)
            .AddScoped("ChatId", chatId)
            .BeginScope();

        _logger.LogDebug("Received {@Message}, handling the message.", message);

        if (isPrivate)
        {
            _logger.LogDebug("Chat is private, adding bot ID to chat ID so that its separate from other bots.");
            chatId += $"${botId}"; // Make separate chats for every bot, when talking to it in private. $ is a hack to split it later.
        }

        var chat = await InitializeChatAndBotAsync(botId, chatId, botFactory, cancellationToken: cancellationToken);

        chat.HandleMessage(message);

        _logger.LogInformation("Successfully handled message.");
    }

    private async ValueTask<IFoulChat> GetOrAddFoulChatAsync(string chatId, CancellationToken cancellationToken)
    {
        _chats.TryGetValue(chatId, out var chat);
        if (chat != null)
            return chat;

        _logger.LogInformation("Chat is not created yet. Waiting for lock to start creating.");
        await _lock.WaitAsync(cancellationToken);
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
            var longChatId = chatId.Contains('$')
                ? Convert.ToInt64(chatId.Split("$")[0])
                : Convert.ToInt64(chatId);
            chat = _foulChatFactory.Create(new FoulChatId(longChatId.ToString()), chatId.Contains('$'));
            chat = _chats.GetOrAdd(chatId, chat);

            if (chatId.Contains('$'))
                _logger.LogInformation("Created a PRIVATE chat.");

            _chatCache.AddChat(chatId);
            _logger.LogInformation("Successfully created the chat.");

            return chat;
        }
        finally
        {
            _logger.LogInformation("Releasing lock for creating chat.");
            _lock.Release();
        }
    }

    private async ValueTask JoinBotToChatIfNecessaryAsync(string botId, string chatId, IFoulChat chat, FoulChatToBotFactory botFactory, string? invitedBy = null, CancellationToken cancellationToken = default)
    {
        if (_joinedBots.Contains($"{botId}{chatId}"))
            return;

        using var _ = Logger
            .AddScoped("BotId", botId)
            .AddScoped("ChatId", chatId)
            .AddScoped("InvitedBy", invitedBy)
            .BeginScope();

        _logger.LogInformation("Did not find the bot, creating and joining it to the chat. Waiting to acquire lock.");
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Entered lock for creating and joining the bot.");
            if (_joinedBots.Contains($"{botId}{chatId}"))
            {
                _logger.LogInformation("Another thread has added this bot, skipping.");
                return;
            }

            _logger.LogInformation("Creating the bot and joining it to chat.");
            var bot = await botFactory(chat);
            _joinedBots.Add($"{botId}{chatId}");
            _joinedBotsObjects.Add($"{botId}{chatId}", bot);
            _logger.LogInformation("Adding bot to chat operation was performed.");
        }
        finally
        {
            _logger.LogInformation("Releasing lock");
            _lock.Release();
        }
    }

    public async ValueTask GracefullyStopAsync()
    {
        if (_isStopping) return;

        _isStopping = true;
        await DisposeAsync();

        await Task.WhenAll(
            _chats.Values.Select(chat => chat.GracefullyStopAsync()));

        await Task.Delay(TimeSpan.FromSeconds(5));
    }

    public async ValueTask DisposeAsync()
    {
        //if (!_isStopping)
            // TODO: Do this without circular references.
            //await GracefullyStopAsync();

        foreach (var bot in _joinedBotsObjects.Values)
        {
            // TODO: Figure out whether interface should be disposable.
            await ((FoulBotNg)bot).DisposeAsync();
        }

        _lock.Dispose();
    }
}
