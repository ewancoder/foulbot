namespace FoulBot.App;

/// <summary>
/// This class is responsible for initially loading all the bots to chats.
/// </summary>
public sealed class ChatLoader : IDisposable
{
    // Read the same file that is maintained by AllowedChatsProvider.
    private readonly ILogger<ChatLoader> _logger;
    private readonly IFoulBotFactory _botFactory;
    private readonly IAllowedChatsProvider _allowedChatsProvider;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ChatLoader(
        ILogger<ChatLoader> logger,
        IFoulBotFactory botFactory,
        IAllowedChatsProvider allowedChatsProvider)
    {
        _logger = logger;
        _botFactory = botFactory;
        _allowedChatsProvider = allowedChatsProvider;
    }

    public async Task LoadBotToAllChatsAsync(
        IBotMessenger botMessenger,
        ChatPool chatPool,
        FoulBotConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var chatIds = await _allowedChatsProvider.GetAllAllowedChatsAsync();

        await Task.WhenAll(chatIds.Select(chatId =>
        {
            // If chat is private - only add the bot that belongs here.
            if (chatId.IsPrivate && chatId.FoulBotId?.BotId != configuration.BotId)
                return Task.CompletedTask;

            var foulBotId = new FoulBotId(configuration.BotId, configuration.BotName);

            // BotName will not be set in chatId from _allowedChatsProvider.
            var foulChatId = chatId.IsPrivate
                ? chatId with { FoulBotId = foulBotId } // Construct proper FoulBotId with BotName set.
                : chatId;

            return chatPool.InitializeChatAndBotAsync(
                foulChatId,
                foulBotId,
                chat => _botFactory.JoinBotToChatAsync(botMessenger, chat, configuration),
                invitedBy: null,
                cancellationToken: cancellationToken);
        }));
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
