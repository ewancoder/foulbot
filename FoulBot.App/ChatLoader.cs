namespace FoulBot.App;

/// <summary>
/// This class is responsible for initially loading all the bots to chats.
/// </summary>
public sealed class ChatLoader : IChatCache, IDisposable
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

            return chatPool.InitializeChatAndBotAsync(
                configuration.BotId,
                chatId,
                chat => _botFactory.JoinBotToChatAsync(botMessenger, chat, configuration),
                cancellationToken: cancellationToken);
        }));
    }

    // A hacky way to avoid nasty deadlocks.
    /// <summary>
    /// This method is called from the main code whenever bot is added to a new chat.
    /// So we can cache it for after app restarts.
    /// </summary>
    public void AddChat(string chatId)
    {
        _ = Task.Run(async () =>
        {
            await _lock.WaitAsync();
            try
            {
                _chatIds.Add(chatId);
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

    public void Dispose()
    {
        _lock.Dispose();
    }
}
