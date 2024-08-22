namespace FoulBot.App;

/// <summary>
/// This class is responsible for initially loading all the bots to chats.
/// </summary>
public sealed class ChatLoader : IChatStore, IDisposable
{
    private const string FileName = "chats";
    private readonly ILogger<ChatLoader> _logger;
    private readonly IFoulBotFactory _botFactory;
    private readonly HashSet<string> _chatIds; // This is being saved in a file.
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ChatLoader(
        ILogger<ChatLoader> logger,
        IFoulBotFactory botFactory)
    {
        _logger = logger;
        _botFactory = botFactory;

        _chatIds = File.Exists(FileName)
            ? [.. File.ReadAllText(FileName).Split(',')] // HACK: Blocking call.
            : [];
    }

    public Task LoadBotToAllChatsAsync(
        IBotMessenger botMessenger,
        ChatPool chatPool,
        FoulBotConfiguration configuration,
        CancellationToken cancellationToken)
    {
        return Task.WhenAll(_chatIds.Select(chatId =>
        {
            // If chat is private - only add the bot that belongs there.
            if (chatId.Contains('$') && chatId.Split('$')[1] != configuration.BotId)
                return Task.CompletedTask;

            var foulBotId = new FoulBotId(configuration.BotId, configuration.BotName);
            FoulChatId foulChatId = chatId.Contains('$')
                ? new(chatId.Split('$')[0]) { FoulBotId = foulBotId }
                : new(chatId);

            return chatPool.InitializeChatAndBotAsync(
                foulChatId,
                foulBotId,
                chat => _botFactory.JoinBotToChatAsync(botMessenger, chat, configuration),
                invitedBy: null,
                cancellationToken: cancellationToken);
        }));
    }

    // A hacky way to avoid nasty deadlocks.
    /// <summary>
    /// This method is called from the main code whenever bot is added to a new chat.
    /// So we can cache it for after app restarts.
    /// </summary>
    public void AddChat(FoulChatId chatId)
    {
        _ = Task.Run(async () =>
        {
            await _lock.WaitAsync();
            try
            {
                _chatIds.Add(GetUniqueKey(chatId));
                await File.WriteAllTextAsync(FileName, string.Join(',', _chatIds));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error when trying to cache chats to file.");
                throw;
            }
            finally
            {
                _lock.Release();
            }
        });
    }

    private static string GetUniqueKey(FoulChatId chatId)
    {
        if (!chatId.IsPrivate)
            return chatId.Value;

        return $"{chatId.Value}${chatId.FoulBotId?.BotId}";
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
